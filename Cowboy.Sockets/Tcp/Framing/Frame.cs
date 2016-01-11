﻿using System;

namespace Cowboy.Sockets
{
    // This wire format for the data transfer part is described by the ABNF.
    // A high-level overview of the framing is given in the following figure. 
    //  0                   1                   2                   3
    //  0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1
    // +-+-------------+-----------------------------------------------+
    // |M| Payload len |    Extended payload length                    |
    // |A|     (7)     |             (16/64)                           |
    // |S|             |   (if payload len==126/127)                   |
    // |K|             |                                               |
    // +-+-------------+- - - - - - - - - - - - - - - - - - - - - - - -+
    // |     Extended payload length continued, if payload len == 127  |
    // + - - - - - - - + - - - - - - - - - - - - - - - - - - - - - - - +
    // |               |    Masking-key, if MASK set to 1              |
    // +---------------+- - - - - - - - - - - - - - - - - - - - - - - -+
    // |               |          Payload Data                         :
    // +----------------- - - - - - - - - - - - - - - - - - - - - - - -+
    // :                     Payload Data continued ...                :
    // + - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - +
    // |                     Payload Data continued ...                |
    // +---------------------------------------------------------------+
    internal sealed class Frame
    {
        private static readonly Random _rng = new Random(DateTime.UtcNow.Millisecond);
        private static readonly int MaskingKeyLength = 4;

        public static byte[] Encode(byte[] playload, int offset, int count, bool isMasked = false)
        {
            byte[] fragment;

            // Payload length:  7 bits, 7+16 bits, or 7+64 bits.
            // The length of the "Payload data", in bytes: 
            // if 0-125, that is the payload length.  
            // If 126, the following 2 bytes interpreted as a 16-bit unsigned integer are the payload length.  
            // If 127, the following 8 bytes interpreted as a 64-bit unsigned integer are the payload length.
            if (count < 126)
            {
                fragment = new byte[1 + (isMasked ? MaskingKeyLength : 0) + count];
                fragment[0] = (byte)count;
            }
            else if (count < 65536)
            {
                fragment = new byte[1 + 2 + (isMasked ? MaskingKeyLength : 0) + count];
                fragment[0] = (byte)126;
                fragment[1] = (byte)(count / 256);
                fragment[2] = (byte)(count % 256);
            }
            else
            {
                fragment = new byte[1 + 8 + (isMasked ? MaskingKeyLength : 0) + count];
                fragment[0] = (byte)127;

                int left = count;
                for (int i = 8; i > 0; i--)
                {
                    fragment[i] = (byte)(left % 256);
                    left = left / 256;

                    if (left == 0)
                        break;
                }
            }

            // Mask:  1 bit
            // Defines whether the "Payload data" is masked.
            if (isMasked)
                fragment[0] = (byte)(fragment[0] | 0x80);

            // Masking-key:  0 or 4 bytes
            // The masking key is a 32-bit value chosen at random by the client.
            if (isMasked)
            {
                int maskingKeyIndex = fragment.Length - (MaskingKeyLength + count);
                for (var i = maskingKeyIndex; i < maskingKeyIndex + MaskingKeyLength; i++)
                {
                    fragment[i] = (byte)_rng.Next(0, 255);
                }
                if (count > 0)
                {
                    int payloadIndex = fragment.Length - count;
                    for (var i = 0; i < count; i++)
                    {
                        fragment[payloadIndex + i] = (byte)(playload[offset + i] ^ fragment[maskingKeyIndex + i % MaskingKeyLength]);
                    }
                }
            }
            else
            {
                if (count > 0)
                {
                    int payloadIndex = fragment.Length - count;
                    for (var i = 0; i < count; i++)
                    {
                        fragment[payloadIndex + i] = playload[offset + i];
                    }
                }
            }

            return fragment;
        }

        public sealed class Header
        {
            public bool IsMasked { get; set; }
            public int PayloadLength { get; set; }
            public int MaskingKeyOffset { get; set; }
            public int Length { get; set; }
        }

        public static Header DecodeHeader(byte[] buffer, int count)
        {
            if (count < 1)
                return null;

            // parse fixed header
            var header = new Header()
            {
                IsMasked = ((buffer[0] & 0x80) == 0x80),
                PayloadLength = (buffer[0] & 0x7f),
                Length = 1,
            };

            // parse extended payload length
            if (header.PayloadLength >= 126)
            {
                if (header.PayloadLength == 126)
                    header.Length += 2;
                else
                    header.Length += 8;

                if (count < header.Length)
                    return null;

                if (header.PayloadLength == 126)
                {
                    header.PayloadLength = buffer[1] * 256 + buffer[2];
                }
                else
                {
                    int totalLength = 0;
                    int level = 1;

                    for (int i = 7; i >= 0; i--)
                    {
                        totalLength += buffer[i + 1] * level;
                        level *= 256;
                    }

                    header.PayloadLength = totalLength;
                }
            }

            // parse masking key
            if (header.IsMasked)
            {
                if (count < header.Length + MaskingKeyLength)
                    return null;

                header.MaskingKeyOffset = header.Length;
                header.Length += MaskingKeyLength;
            }

            return header;
        }

        public static byte[] DecodeMaskedPayload(byte[] buffer, int maskingKeyOffset, int payloadOffset, int payloadCount)
        {
            var payload = new byte[payloadCount];

            for (var i = 0; i < payloadCount; i++)
            {
                payload[i] = (byte)(buffer[payloadOffset + i] ^ buffer[maskingKeyOffset + i % MaskingKeyLength]);
            }

            return payload;
        }
    }
}
