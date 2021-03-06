﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

using System.Runtime.Serialization.Formatters.Binary;
using System.IO;

namespace SamSeifert.Velodyne
{
    public class VLP_16
    {
        public delegate void PacketRecieved(Packet p, IPEndPoint ip);
        public delegate bool ShouldCancel(UpdateArgs a);

        /// <summary>
        /// This constructor won't return untill listener is done (or error).
        /// It returns the raw data packets sent by the VLP-16
        /// Throws a socket exception only on initialization.  Once everything is up and running exceptions are handled internally.
        /// </summary>
        /// <param name="d"></param>
        /// <param name="packet_recieved_sync"></param>
        /// <param name="should_cancel_async">Called at 1 Hz</param>
        public VLP_16(
            IPEndPoint d,
            PacketRecieved packet_recieved_sync = null,
            ShouldCancel should_cancel_async = null)
        {
            UdpClient cl = null;
            try
            {
                cl = new UdpClient(new IPEndPoint(d.Address, d.Port));
                cl.Client.ReceiveTimeout = 250; // .25 Seconds
                cl.Client.ReceiveBufferSize = 4096;

                this.Listen(cl, packet_recieved_sync, should_cancel_async); // This will only end when canceled
            }
            finally
            {
                if (cl != null)
                {
                    cl.Dispose();
                }
            }
        }



        private unsafe void Listen(
            UdpClient cl,
            PacketRecieved packet_recieved_sync,
            ShouldCancel should_cancel_async)
        {
            DateTime start = DateTime.Now;
            int elapsed_seconds = 0;

            int corrects = 0;
            int incorrects = 0;
            int timeouts = 0;
            int socketerrors = 0;

            while (true)
            {
                try
                {
                    IPEndPoint iep = null;
                    var data = cl.Receive(ref iep);

                    if (data.Length == Packet.Raw._Size)
                    {
                        Packet p;
                        bool valid;

                        fixed (byte* b = data)
                            p = new Packet(b, out valid);

                        if (valid)
                        {
                            corrects++;

                            if (packet_recieved_sync != null)
                            {
                                packet_recieved_sync(p, iep);
                            }
                        }
                        else
                        {
                            incorrects--;
                        }
                    }
                    else
                    {
                        incorrects++;
                    }
                }
                catch (TimeoutException)
                {
                    timeouts++;
                }
                catch (SocketException)
                {
                    socketerrors++;
                }
                catch (Exception e)
                {
                    System.Diagnostics.Debug.WriteLine(this.GetType().FullName + ": " + e.ToString());
                    incorrects++;
                }

                var now = DateTime.Now;
                if ((now - start).TotalSeconds > elapsed_seconds)
                {
                    elapsed_seconds++;

                    var st = new UpdateArgs();

                    st.SocketErrors = timeouts;
                    st.PacketsReceivedCorrectly = corrects;
                    st.PacketsReceivedIncorrectly = incorrects;
                    st.SocketErrors = socketerrors;

                    timeouts = 0;
                    corrects = 0;
                    incorrects = 0;
                    socketerrors = 0;

                    if (should_cancel_async != null)
                        if (should_cancel_async(st))
                        {
                            return;
                        }
                }
            }
        }

        public static readonly float[] LaserPitchSin; // Do this map once and hopefully save time!
        public static readonly float[] LaserPitchCos;
        public static readonly float[] LaserPitch = // Taken from datasheet
        {
            -15,
            1,
            -13,
            3,
            -11,
            5,
            -9,
            7,
            -7,
            9,
            -5,
            11,
            -3,
            13,
            -1,
            15                
        };

        static VLP_16()
        {
            int lens = LaserPitch.Length;
            LaserPitchCos = new float[lens];
            LaserPitchSin = new float[lens];
            for (int i = 0; i < lens; i++)
            {
                float angle_radians = UnitConverter.DegreesToRadians(LaserPitch[i]);
                LaserPitchCos[i] = (float)Math.Cos(angle_radians);
                LaserPitchSin[i] = (float)Math.Sin(angle_radians);
            }
        }

        #region Data Structures Taken From Data Sheet
        public class Packet : JsonPackable
        {
            private static HashSet<int> _BadReturnTypes = new HashSet<int>();
            private static HashSet<int> _BadLidarTypes = new HashSet<int>();

            public VerticalBlockPair[] _Blocks { get; private set; }
            public uint _Time { get; private set; }
            public VelodyneModel _VelodyneModel { get; private set; }
            public ReturnType _ReturnType { get; private set; }

            public JsonDict Pack()
            {
                var of_the_jedi = new JsonDict();

                of_the_jedi["Time"] = this._Time;
                of_the_jedi["VelodyneModel"] = (int)this._VelodyneModel;
                of_the_jedi["ReturnType"] = (int)this._ReturnType;
                of_the_jedi["Blocks"] = Array.ConvertAll(this._Blocks, item => (object)item.Pack());

                return of_the_jedi;
            }

            public void Unpack(JsonDict dict)
            {
                // All numeric entities will be doubles
                // Round and cast them
                this._Time = (uint)Math.Round((double)dict["Time"]);
                this._VelodyneModel = (VelodyneModel)(int)Math.Round((double)dict["VelodyneModel"]);
                this._ReturnType = (ReturnType)(int)Math.Round((double)dict["ReturnType"]);
                this._Blocks = Array.ConvertAll(dict["Blocks"] as object[], item => new VerticalBlockPair(item as JsonDict));
            }

            public Packet(JsonDict d)
            {
                this.Unpack(d);
            }

            internal unsafe Packet(Byte* b, out bool valid)
            {
                Raw r = *(Raw*)b;

                this._Time = 0;
                for (int i = 4; i > 0; i--)
                    this._Time = (this._Time << 8) | r._TimeStamp[i - 1];

                int raw_rt = r._Factory[0];
                int raw_lt = r._Factory[1];

                switch (raw_rt)
                {
                    case 0x37:
                        this._ReturnType = ReturnType.Strongest;
                        break;
                    case 0x38:
                        this._ReturnType = ReturnType.Last;
                        break;
                    case 0x39:
                        this._ReturnType = ReturnType.Dual;
                        break;
                    default:
                        this._ReturnType = ReturnType.NAN;
                        bool added = false;
                        lock (_BadReturnTypes)
                            if (!_BadReturnTypes.Contains(raw_rt))
                            {
                                _BadReturnTypes.Add(raw_rt);
                                added = true;
                            }

                        if (added)
                            System.Diagnostics.Debug.WriteLine("Unrecognized Return Type: " + raw_rt);

                        break;
                }

                switch (raw_lt)
                {
                    case 0x21:
                        this._VelodyneModel = VelodyneModel.HDL_32E;
                        break;
                    case 0x22:
                        this._VelodyneModel = VelodyneModel.VLP_16;
                        break;
                    default:
                        this._VelodyneModel = VelodyneModel.NAN;
                        bool added = false;
                        lock (_BadReturnTypes)
                        if (!_BadLidarTypes.Contains(raw_lt))
                            {
                                _BadLidarTypes.Add(raw_lt);
                                added = true;
                            }

                        if (added)
                            System.Diagnostics.Debug.WriteLine("Unrecognized Lidar Type: " + raw_lt);

                        break;
                }

                valid = true;

                this._Blocks = new VerticalBlockPair[Raw._VerticalBlockPairsPerPacket];

                for (int i = 0, byte_index = 0;
                    i < Raw._VerticalBlockPairsPerPacket;
                    i++, byte_index += VerticalBlockPair.Raw._Size)
                {
                    bool bb;
                    this._Blocks[i] = new VerticalBlockPair(&r._DataBlocks[byte_index], out bb);
                    valid &= bb;
                }
            }

            [StructLayout(LayoutKind.Explicit, Pack = 1)]
            internal unsafe struct Raw
            {
                public const int _Size = _FactoryE;
                public const int _VerticalBlockPairsPerPacket = 12;

                // private const int _HeaderE = 42;
                private const int _DataBlockE = VerticalBlockPair.Raw._Size * _VerticalBlockPairsPerPacket;
                private const int _TimeStampE = 4 + _DataBlockE;
                private const int _FactoryE = 2 + _TimeStampE;

                // [FieldOffset(0)]
                // public fixed byte _Header[42]; // UDP Will Filter This Out

                [FieldOffset(0)]
                public fixed byte _DataBlocks[VerticalBlockPair.Raw._Size * _VerticalBlockPairsPerPacket];

                [FieldOffset(_DataBlockE)]
                public fixed byte _TimeStamp[4];

                [FieldOffset(_TimeStampE)]
                public fixed byte _Factory[2];
            };
        }

        public struct VerticalBlockPair : JsonPackable
        {
            public float _Azimuth { get; private set; }
            public SinglePoint[] _ChannelData { get; private set; }

            internal unsafe VerticalBlockPair(Byte* b, out bool valid)
            {
                Raw r = *(Raw*)b;

                valid = (r._Header[0] == 0xFF) && (r._Header[1] == 0xEE);

                this._Azimuth = ((r._Azimuth[1] << 8) | (r._Azimuth[0])) / 100.0f;

                this._ChannelData = new SinglePoint[Raw._SinglePointsPerVerticalBlock];

                for (int i = 0, byte_index = 0;
                    i < Raw._SinglePointsPerVerticalBlock;
                    i++, byte_index += SinglePoint.Raw._Size)
                    this._ChannelData[i] = new SinglePoint(&r._ChannelData[byte_index]);
            }

            internal VerticalBlockPair(JsonDict dict)
            {
                this._Azimuth = 0;
                this._ChannelData = null;
                this.Unpack(dict);
            }

            public JsonDict Pack()
            {
                var of_the_jedi = new JsonDict();
                of_the_jedi["Azimuth"] = this._Azimuth;
                of_the_jedi["Length"] = this._ChannelData.Length;
                of_the_jedi["Distances"] = Array.ConvertAll(this._ChannelData, item => (object)item._DistanceMeters);
                of_the_jedi["Reflectivities"] = Array.ConvertAll(this._ChannelData, item => (object)item._Reflectivity);
                return of_the_jedi;
            }

            public void Unpack(JsonDict dict)
            {
                this._Azimuth = (float)(double)dict["Azimuth"];

                int lens = (int)(double)dict["Length"];

                this._ChannelData = new SinglePoint[lens];

                var distances = dict["Distances"] as object[];
                var reflectivities = dict["Reflectivities"] as object[];

                for (int i = 0; i < lens; i++)
                    this._ChannelData[i] = new SinglePoint(
                            (float)(double)distances[i],
                            (byte)Math.Round((double)reflectivities[i])
                        );
            }

            [StructLayout(LayoutKind.Explicit, Pack = 1)]
            internal unsafe struct Raw
            {
                public const int _SinglePointsPerVerticalBlock = 32;
                public const int _Size = _ChannelDataE;

                private const int _HeaderE = 2;
                private const int _AzimuthE = 2 + _HeaderE;
                private const int _ChannelDataE = SinglePoint.Raw._Size * _SinglePointsPerVerticalBlock + _AzimuthE;

                [FieldOffset(0)]
                public fixed byte _Header[2];

                [FieldOffset(_HeaderE)]
                public fixed byte _Azimuth[2];

                [FieldOffset(_AzimuthE)]
                public fixed byte _ChannelData[SinglePoint.Raw._Size * _SinglePointsPerVerticalBlock];
            }
        }

        [Serializable]
        public struct SinglePoint
        {
            public readonly float _DistanceMeters;
            public readonly byte _Reflectivity;

            internal unsafe SinglePoint(Byte* b)
            {
                Raw r = *(Raw*)b;

                // Return Azimuth
                this._DistanceMeters = ((r._Distance[1] << 8) | (r._Distance[0])) * 0.002f; // 2 MM increments
                this._Reflectivity = r._Reflectivity;
            }

            internal SinglePoint(float distance_meters, byte reflectivity)
            {
                this._DistanceMeters = distance_meters;
                this._Reflectivity = reflectivity;
            }
            

            [StructLayout(LayoutKind.Explicit, Pack = 1)]
            internal unsafe struct Raw
            {
                public const int _Size = 3;

                [FieldOffset(0)]
                public fixed byte _Distance[2];

                [FieldOffset(2)]
                public byte _Reflectivity;
            };
        }
        #endregion
    }
}
