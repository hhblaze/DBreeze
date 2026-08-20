/*
  Copyright (C) 2012 dbreeze.tiesky.com / Alex Solovyov / Ivars Sudmalis.
  It's free software for those who think that it should be free.
*/

using System;
using System.Collections.Generic;

namespace DBreeze.LianaTrie.Iterations
{
    public class Backward
    {
        private readonly LTrieRootNode _root;
        private bool ReturnKeyValuePair = false;

        public Backward(LTrieRootNode root, bool ValuesLazyLoadingIsOn)
        {
            _root = root;
            ReturnKeyValuePair = !ValuesLazyLoadingIsOn;
        }

        private struct BackwardRangeFrame
        {
            public IEnumerator<LTrieKid> Iterator;
            public int Depth;
            public int StartRelation;
            public int StopRelation;
        }

        private static int AdvanceBoundRelation(int relation, int depth, int kid, byte[] bound)
        {
            if (relation != 0)
                return relation;
            if (depth >= bound.Length)
                return 1;
            return kid.CompareTo(bound[depth]);
        }

        private static int AdvanceLeafRelation(
            int relation, int depth, LTrieKid kid, byte[] bound)
        {
            if (relation != 0)
                return relation;

            // ValueKid terminates the key at this generation; its Val == 256 is a
            // storage sentinel, not a byte that participates in key ordering.
            if (kid.ValueKid)
                return depth.CompareTo(bound.Length);

            return AdvanceBoundRelation(relation, depth, kid.Val, bound);
        }

        private static int CompareKeys(byte[] left, byte[] right)
        {
            int commonLength = Math.Min(left.Length, right.Length);
            for (int i = 0; i < commonLength; i++)
            {
                int comparison = left[i].CompareTo(right[i]);
                if (comparison != 0)
                    return comparison;
            }

            return left.Length.CompareTo(right.Length);
        }

        private static byte[] GetLexicographicSuccessor(byte[] prefix)
        {
            for (int i = prefix.Length - 1; i >= 0; i--)
            {
                if (prefix[i] == byte.MaxValue)
                    continue;

                byte[] successor = new byte[i + 1];
                Buffer.BlockCopy(prefix, 0, successor, 0, i + 1);
                successor[i]++;
                return successor;
            }

            return null;
        }

        private static void ValidateNonEmptyStartKey(byte[] key)
        {
            if (key.Length == 0)
                throw new IndexOutOfRangeException();
        }

        private LTrieRow ReadRow(LTrieKid kid, bool useCache)
        {
            LTrieRow row = new LTrieRow(_root);
            if (ReturnKeyValuePair)
            {
                long valueStartPtr;
                uint valueLength;
                byte[] key;
                byte[] value;
                _root.Tree.Cache.ReadKeyValue(useCache, kid.Ptr, out valueStartPtr, out valueLength, out key, out value);
                row.ValueStartPointer = valueStartPtr;
                row.ValueFullLength = valueLength;
                row.Value = value;
                row.ValueIsReadOut = true;
                row.Key = key;
            }
            else
            {
                row.Key = _root.Tree.Cache.ReadKey(useCache, kid.Ptr);
            }

            row.LinkToValue = kid.Ptr;
            return row;
        }

        private static IEnumerator<LTrieKid> GetBackwardIterator(
            LTrieGenerationNode node,
            int depth,
            int startRelation,
            byte[] startKey,
            bool hasStart)
        {
            if (!hasStart || startRelation != 0)
                return node.KidsInNode.GetKidsBackward().GetEnumerator();

            int startFrom = depth < startKey.Length ? startKey[depth] : 256;
            return node.KidsInNode.GetKidsBackward(startFrom).GetEnumerator();
        }

        private IEnumerable<LTrieRow> IterateBackwardCore(bool useCache)
        {
            LTrieGenerationNode rootNode = new LTrieGenerationNode(_root);
            rootNode.Pointer = _root.LinkToZeroNode;
            rootNode.ReadSelf(useCache, null);

            Stack<IEnumerator<LTrieKid>> stack = new Stack<IEnumerator<LTrieKid>>();
            stack.Push(rootNode.KidsInNode.GetKidsBackward().GetEnumerator());

            try
            {
                while (stack.Count > 0)
                {
                    IEnumerator<LTrieKid> iterator = stack.Peek();
                    if (!iterator.MoveNext())
                    {
                        iterator.Dispose();
                        stack.Pop();
                        continue;
                    }

                    LTrieKid kid = iterator.Current;
                    if (kid.ValueKid || !kid.LinkToNode)
                    {
                        yield return ReadRow(kid, useCache);
                    }
                    else
                    {
                        LTrieGenerationNode node = new LTrieGenerationNode(_root);
                        node.Pointer = kid.Ptr;
                        node.Value = (byte)kid.Val;
                        node.ReadSelf(useCache, null);
                        stack.Push(node.KidsInNode.GetKidsBackward().GetEnumerator());
                    }
                }
            }
            finally
            {
                while (stack.Count > 0)
                    stack.Pop().Dispose();
            }
        }

        private IEnumerable<LTrieRow> IterateBackwardRangeCore(
            byte[] startKey,
            bool hasStart,
            bool includeStart,
            byte[] stopKey,
            bool hasStop,
            bool includeStop,
            ulong remainingToSkip,
            bool useCache)
        {
            LTrieGenerationNode rootNode = new LTrieGenerationNode(_root);
            rootNode.Pointer = _root.LinkToZeroNode;
            rootNode.ReadSelf(useCache, null);

            Stack<BackwardRangeFrame> stack = new Stack<BackwardRangeFrame>();
            stack.Push(new BackwardRangeFrame
            {
                Iterator = GetBackwardIterator(rootNode, 0, 0, startKey, hasStart),
                Depth = 0,
                StartRelation = 0,
                StopRelation = 0,
            });

            try
            {
                while (stack.Count > 0)
                {
                    BackwardRangeFrame frame = stack.Peek();
                    if (!frame.Iterator.MoveNext())
                    {
                        frame.Iterator.Dispose();
                        stack.Pop();
                        continue;
                    }

                    LTrieKid kid = frame.Iterator.Current;
                    if (kid.ValueKid || !kid.LinkToNode)
                    {
                        int leafStartRelation = frame.StartRelation;
                        if (hasStart)
                            leafStartRelation = AdvanceLeafRelation(
                                leafStartRelation, frame.Depth, kid, startKey);

                        int leafStopRelation = frame.StopRelation;
                        if (hasStop)
                            leafStopRelation = AdvanceLeafRelation(
                                leafStopRelation, frame.Depth, kid, stopKey);

                        if (remainingToSkip > 0)
                        {
                            byte[] key = null;

                            if (hasStart)
                            {
                                if (leafStartRelation > 0)
                                    continue;
                                if (leafStartRelation == 0)
                                {
                                    key = _root.Tree.Cache.ReadKey(useCache, kid.Ptr);
                                    int startComparison = CompareKeys(key, startKey);
                                    if (startComparison > 0 || (startComparison == 0 && !includeStart))
                                        continue;
                                }
                            }

                            if (hasStop)
                            {
                                if (leafStopRelation < 0)
                                    yield break;
                                if (leafStopRelation == 0)
                                {
                                    if (key == null)
                                        key = _root.Tree.Cache.ReadKey(useCache, kid.Ptr);
                                    int stopComparison = CompareKeys(key, stopKey);
                                    if (stopComparison < 0 || (stopComparison == 0 && !includeStop))
                                        yield break;
                                }
                            }

                            remainingToSkip--;
                            continue;
                        }

                        LTrieRow row = ReadRow(kid, useCache);

                        if (hasStart)
                        {
                            if (leafStartRelation > 0)
                                continue;
                            if (leafStartRelation == 0)
                            {
                                int startComparison = CompareKeys(row.Key, startKey);
                                if (startComparison > 0 || (startComparison == 0 && !includeStart))
                                    continue;
                            }
                        }

                        if (hasStop)
                        {
                            if (leafStopRelation < 0)
                                yield break;
                            if (leafStopRelation == 0)
                            {
                                int stopComparison = CompareKeys(row.Key, stopKey);
                                if (stopComparison < 0 || (stopComparison == 0 && !includeStop))
                                    yield break;
                            }
                        }

                        yield return row;
                        continue;
                    }

                    int startRelation = frame.StartRelation;
                    if (hasStart)
                    {
                        startRelation = AdvanceBoundRelation(startRelation, frame.Depth, kid.Val, startKey);
                        if (startRelation > 0)
                            continue;
                    }

                    int stopRelation = frame.StopRelation;
                    if (hasStop)
                    {
                        stopRelation = AdvanceBoundRelation(stopRelation, frame.Depth, kid.Val, stopKey);
                        if (stopRelation < 0)
                            yield break;
                    }

                    LTrieGenerationNode node = new LTrieGenerationNode(_root);
                    node.Pointer = kid.Ptr;
                    node.Value = (byte)kid.Val;
                    node.ReadSelf(useCache, null);
                    stack.Push(new BackwardRangeFrame
                    {
                        Iterator = GetBackwardIterator(node, frame.Depth + 1, startRelation, startKey, hasStart),
                        Depth = frame.Depth + 1,
                        StartRelation = startRelation,
                        StopRelation = stopRelation,
                    });
                }
            }
            finally
            {
                while (stack.Count > 0)
                    stack.Pop().Iterator.Dispose();
            }
        }

        private IEnumerable<LTrieRow> IterateBackwardStartFromCore(
            byte[] initKey,
            bool inclStartKey,
            ulong remainingToSkip,
            bool useCache)
        {
            // Preserve the historical delayed exception for an empty start key.
            ValidateNonEmptyStartKey(initKey);

            foreach (LTrieRow row in IterateBackwardRangeCore(
                initKey, true, inclStartKey, null, false, false, remainingToSkip, useCache))
            {
                yield return row;
            }
        }

        private IEnumerable<LTrieRow> IterateBackwardSkipCore(ulong remainingToSkip, bool useCache)
        {
            LTrieGenerationNode rootNode = new LTrieGenerationNode(_root);
            rootNode.Pointer = _root.LinkToZeroNode;
            rootNode.ReadSelf(useCache, null);

            Stack<IEnumerator<LTrieKid>> stack = new Stack<IEnumerator<LTrieKid>>();
            stack.Push(rootNode.KidsInNode.GetKidsBackward().GetEnumerator());

            try
            {
                while (stack.Count > 0)
                {
                    IEnumerator<LTrieKid> iterator = stack.Peek();
                    if (!iterator.MoveNext())
                    {
                        iterator.Dispose();
                        stack.Pop();
                        continue;
                    }

                    LTrieKid kid = iterator.Current;
                    if (kid.ValueKid || !kid.LinkToNode)
                    {
                        if (remainingToSkip > 0)
                        {
                            remainingToSkip--;
                            continue;
                        }

                        yield return ReadRow(kid, useCache);
                    }
                    else
                    {
                        LTrieGenerationNode node = new LTrieGenerationNode(_root);
                        node.Pointer = kid.Ptr;
                        node.Value = (byte)kid.Val;
                        node.ReadSelf(useCache, null);
                        stack.Push(node.KidsInNode.GetKidsBackward().GetEnumerator());
                    }
                }
            }
            finally
            {
                while (stack.Count > 0)
                    stack.Pop().Dispose();
            }
        }

        public IEnumerable<LTrieRow> IterateBackward(bool useCache)
        {
            return IterateBackwardCore(useCache);
        }

        public IEnumerable<LTrieRow> IterateBackwardStartFrom(byte[] initKey, bool inclStartKey, bool useCache)
        {
            return IterateBackwardStartFromCore(initKey, inclStartKey, 0, useCache);
        }

        public IEnumerable<LTrieRow> IterateBackwardFromTo(
            byte[] initKey,
            byte[] stopKey,
            bool inclStartKey,
            bool inclStopKey,
            bool useCache)
        {
            return IterateBackwardRangeCore(
                initKey, true, inclStartKey, stopKey, true, inclStopKey, 0, useCache);
        }

        public LTrieRow IterateBackwardForMaximal(bool useCache)
        {
            LTrieGenerationNode node = new LTrieGenerationNode(_root);
            node.Pointer = _root.LinkToZeroNode;
            node.ReadSelf(useCache, null);

            while (true)
            {
                LTrieKid firstKid = null;
                using (IEnumerator<LTrieKid> iterator = node.KidsInNode.GetKidsBackward().GetEnumerator())
                {
                    if (iterator.MoveNext())
                        firstKid = iterator.Current;
                }

                if (firstKid == null)
                    return new LTrieRow(_root);
                if (firstKid.ValueKid || !firstKid.LinkToNode)
                    return ReadRow(firstKid, useCache);

                node = new LTrieGenerationNode(_root);
                node.Pointer = firstKid.Ptr;
                node.Value = (byte)firstKid.Val;
                node.ReadSelf(useCache, null);
            }
        }

        public IEnumerable<LTrieRow> IterateBackwardSkipFrom(byte[] initKey, ulong skippingQuantity, bool useCache)
        {
            return IterateBackwardStartFromCore(initKey, false, skippingQuantity, useCache);
        }

        public IEnumerable<LTrieRow> IterateBackwardSkip(ulong skippingQuantity, bool useCache)
        {
            return IterateBackwardSkipCore(skippingQuantity, useCache);
        }

        public IEnumerable<LTrieRow> IterateBackwardStartsWith(byte[] initKey, bool useCache)
        {
            if (initKey.Length < 1)
                yield break;

            byte[] successor = GetLexicographicSuccessor(initKey);
            foreach (LTrieRow row in IterateBackwardRangeCore(
                successor, successor != null, false, initKey, true, true, 0, useCache))
            {
                yield return row;
            }
        }

        public IEnumerable<LTrieRow> IterateBackwardStartsWithClosestToPrefix(byte[] initKey, bool useCache)
        {
            if (initKey.Length < 1)
                return new List<LTrieRow>();

            Forward fw = new Forward(_root, !ReturnKeyValuePair);
            fw.IterateForwardStartsWith_Prefix_Helper(initKey, useCache);

            if (fw.PrefixDeep == -1)
                return new List<LTrieRow>();

            byte[] newKey = new byte[fw.PrefixDeep + 1];
            Buffer.BlockCopy(initKey, 0, newKey, 0, fw.PrefixDeep + 1);
            return IterateBackwardStartsWith(newKey, useCache);
        }
    }
}
