/*
  Copyright (C) 2012 dbreeze.tiesky.com / Alex Solovyov / Ivars Sudmalis.
  It's free software for those who think that it should be free.
*/

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace DBreeze.Storage
{
    /// <summary>
    /// .NET 8 sorted, non-overlapping last-write-wins pending-write view.
    /// </summary>
    internal sealed class BufferedWriteSet
    {
        internal struct Segment
        {
            internal long Offset;
            internal long End;
            internal byte[] Buffer;
            internal int BufferOffset;
            internal int Length;
        }

        private readonly List<Segment> _segments = new List<Segment>();
        private int _writeOperations;

        internal int Count => _segments.Count;
        internal int WriteOperations => _writeOperations;
        internal Segment this[int index] => _segments[index];
        internal ref readonly Segment GetSegment(int index) => ref CollectionsMarshal.AsSpan(_segments)[index];

        internal void Add(long offset, byte[] buffer)
        {
            ArgumentNullException.ThrowIfNull(buffer);
            ArgumentOutOfRangeException.ThrowIfNegative(offset);

            long end = checked(offset + buffer.Length);
            _writeOperations = checked(_writeOperations + 1);
            if (buffer.Length == 0)
                return;

            Segment incoming = new Segment
            {
                Offset = offset,
                End = end,
                Buffer = buffer,
                BufferOffset = 0,
                Length = buffer.Length
            };
            int segmentCount = _segments.Count;
            if (segmentCount == 0 || _segments[segmentCount - 1].End <= offset)
            {
                _segments.Add(incoming);
                return;
            }

            int first = LowerBound(offset);
            if (first > 0 && _segments[first - 1].End > offset)
                first--;
            while (first < _segments.Count && _segments[first].End <= offset)
                first++;

            int scan = first;
            Segment left = default;
            Segment right = default;
            bool hasLeft = false;
            bool hasRight = false;
            Span<Segment> segments = CollectionsMarshal.AsSpan(_segments);

            while (scan < segments.Length && segments[scan].Offset < end)
            {
                Segment old = segments[scan];
                if (!hasLeft && old.Offset < offset)
                {
                    int leftLength = checked((int)(offset - old.Offset));
                    left = Slice(old, old.Offset, old.BufferOffset, leftLength);
                    hasLeft = leftLength != 0;
                }

                if (old.End > end)
                {
                    int skipped = checked((int)(end - old.Offset));
                    int rightLength = checked((int)(old.End - end));
                    right = Slice(old, end, checked(old.BufferOffset + skipped), rightLength);
                    hasRight = rightLength != 0;
                }
                scan++;
            }

            int removeCount = scan - first;
            if (removeCount != 0)
                _segments.RemoveRange(first, removeCount);

            int insertAt = first;
            if (hasLeft)
                _segments.Insert(insertAt++, left);
            _segments.Insert(insertAt++, incoming);
            if (hasRight)
                _segments.Insert(insertAt, right);
        }

        internal void Overlay(long offset, byte[] destination)
        {
            ArgumentNullException.ThrowIfNull(destination);
            if (destination.Length == 0 || _segments.Count == 0)
                return;

            long end = checked(offset + destination.Length);
            int index = LowerBound(offset);
            Span<Segment> segments = CollectionsMarshal.AsSpan(_segments);
            if (index > 0 && segments[index - 1].End > offset)
                index--;

            for (; index < segments.Length; index++)
            {
                ref readonly Segment segment = ref segments[index];
                if (segment.Offset >= end)
                    break;
                if (segment.End <= offset)
                    continue;

                long copyStart = Math.Max(segment.Offset, offset);
                long copyEnd = Math.Min(segment.End, end);
                segment.Buffer.AsSpan(
                    checked(segment.BufferOffset + (int)(copyStart - segment.Offset)),
                    checked((int)(copyEnd - copyStart))).CopyTo(
                        destination.AsSpan(checked((int)(copyStart - offset))));
            }
        }

        internal void Clear()
        {
            _segments.Clear();
            _writeOperations = 0;
        }

        private int LowerBound(long offset)
        {
            int low = 0;
            int high = _segments.Count;
            Span<Segment> segments = CollectionsMarshal.AsSpan(_segments);
            while (low < high)
            {
                int middle = low + ((high - low) >> 1);
                if (segments[middle].Offset < offset)
                    low = middle + 1;
                else
                    high = middle;
            }
            return low;
        }

        private static Segment Slice(Segment source, long offset, int bufferOffset, int length) => new Segment
        {
            Offset = offset,
            End = checked(offset + length),
            Buffer = source.Buffer,
            BufferOffset = bufferOffset,
            Length = length
        };
    }
}
