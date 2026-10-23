using System;
using System.Text;
using UnityEngine;

namespace GoingBatty
{
    /// <summary>
    /// Tiny binary writer used for every network message. Kept independent of any
    /// networking library so the transport (Unity Relay today, Steam later) can be swapped.
    /// </summary>
    public sealed class ByteWriter
    {
        byte[] _buf;
        int _len;

        public ByteWriter(int capacity = 256) { _buf = new byte[capacity]; }

        public int Length => _len;

        void Ensure(int extra)
        {
            if (_len + extra <= _buf.Length) return;
            int n = Math.Max(_buf.Length * 2, _len + extra);
            Array.Resize(ref _buf, n);
        }

        public ByteWriter U8(byte v) { Ensure(1); _buf[_len++] = v; return this; }
        public ByteWriter Bool(bool v) => U8(v ? (byte)1 : (byte)0);
        public ByteWriter U16(ushort v) { Ensure(2); _buf[_len++] = (byte)v; _buf[_len++] = (byte)(v >> 8); return this; }
        public ByteWriter I32(int v)
        {
            Ensure(4);
            _buf[_len++] = (byte)v; _buf[_len++] = (byte)(v >> 8);
            _buf[_len++] = (byte)(v >> 16); _buf[_len++] = (byte)(v >> 24);
            return this;
        }
        public ByteWriter U64(ulong v) { I32((int)(v & 0xffffffff)); I32((int)(v >> 32)); return this; }
        public ByteWriter F32(float v)
        {
            int bits = BitConverter.SingleToInt32Bits(v);
            return I32(bits);
        }
        public ByteWriter V3(Vector3 v) { F32(v.x); F32(v.y); F32(v.z); return this; }
        public ByteWriter Str(string s)
        {
            s ??= "";
            var bytes = Encoding.UTF8.GetBytes(s);
            if (bytes.Length > 1000) Array.Resize(ref bytes, 1000);
            U16((ushort)bytes.Length);
            Bytes(bytes, 0, bytes.Length);
            return this;
        }
        public ByteWriter Bytes(byte[] b, int offset, int count)
        {
            Ensure(count);
            Buffer.BlockCopy(b, offset, _buf, _len, count);
            _len += count;
            return this;
        }
        public ByteWriter Blob(byte[] b)
        {
            U16((ushort)b.Length);
            return Bytes(b, 0, b.Length);
        }

        public byte[] ToArray()
        {
            var r = new byte[_len];
            Buffer.BlockCopy(_buf, 0, r, 0, _len);
            return r;
        }
    }

    public sealed class ByteReader
    {
        readonly byte[] _buf;
        int _pos;

        public ByteReader(byte[] buf, int start = 0) { _buf = buf; _pos = start; }

        public bool HasMore => _pos < _buf.Length;

        public byte U8() => _buf[_pos++];
        public bool Bool() => U8() != 0;
        public ushort U16() { ushort v = (ushort)(_buf[_pos] | (_buf[_pos + 1] << 8)); _pos += 2; return v; }
        public int I32()
        {
            int v = _buf[_pos] | (_buf[_pos + 1] << 8) | (_buf[_pos + 2] << 16) | (_buf[_pos + 3] << 24);
            _pos += 4;
            return v;
        }
        public ulong U64() { uint lo = (uint)I32(); uint hi = (uint)I32(); return ((ulong)hi << 32) | lo; }
        public float F32() => BitConverter.Int32BitsToSingle(I32());
        public Vector3 V3() => new Vector3(F32(), F32(), F32());
        public string Str()
        {
            int n = U16();
            var s = Encoding.UTF8.GetString(_buf, _pos, n);
            _pos += n;
            return s;
        }
        public byte[] Blob()
        {
            int n = U16();
            var b = new byte[n];
            Buffer.BlockCopy(_buf, _pos, b, 0, n);
            _pos += n;
            return b;
        }
    }
}
