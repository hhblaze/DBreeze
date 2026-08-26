/*
  Copyright (C) 2012 dbreeze.tiesky.com / Alex Solovyov / Ivars Sudmalis.
  It's free software for those who think that it should be free.
*/

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Xml;

namespace DBreeze.Transactions
{
    /// <summary>
    /// Encodes the transaction participant list stored in _DBreezeTranJrnl.
    ///
    /// Historical .NET Framework builds wrote a rooted ArrayOfString document,
    /// while .NET Standard and later .NET builds wrote a sequence of rootless
    /// string elements. New payloads use the smallest rooted representation that
    /// both historical readers accept. The reader remains compatible with both
    /// historical representations. Cross-baseline compatibility is necessarily
    /// limited to names without XML text metacharacters because the historical
    /// rootless reader did not decode entities; current readers round-trip them.
    /// </summary>
    internal static class TransactionJournalPayloadCodec
    {
        private const string RootElement = "ArrayOfString";
        private const string ItemElement = "string";
        private const string RootStart = "<ArrayOfString";
        private const string ItemStart = "<string>";
        private const string ItemEnd = "</string>";

        internal static string Serialize(IList<string> tableNames)
        {
            if (tableNames == null)
                throw new ArgumentNullException("tableNames");
            if (tableNames.Count == 0)
                throw new InvalidDataException("A transaction journal payload must contain at least one table name.");

            int estimatedCapacity = 35;
            for (int i = 0; i < tableNames.Count; i++)
            {
                string tableName = tableNames[i];
                ValidateTableName(tableName);
                if (estimatedCapacity <= Int32.MaxValue - tableName.Length - 19)
                    estimatedCapacity += tableName.Length + 19;
            }

            StringBuilder payload = new StringBuilder(estimatedCapacity);
            payload.Append("<ArrayOfString>\n");
            for (int i = 0; i < tableNames.Count; i++)
            {
                payload.Append(ItemStart);
                AppendEscapedXmlText(payload, tableNames[i]);
                payload.Append(ItemEnd);
                payload.Append('\n');
            }
            payload.Append("</ArrayOfString>");
            return payload.ToString();
        }

        internal static List<string> Deserialize(string payload)
        {
            if (IsNullOrWhiteSpace(payload))
                throw new InvalidDataException("The transaction journal payload is empty.");

            int offset = SkipWhitespace(payload, 0);
            if (StartsWith(payload, offset, "<?xml") || StartsWith(payload, offset, RootStart))
                return DeserializeRooted(payload);
            if (StartsWith(payload, offset, ItemStart))
                return DeserializeLegacyFragment(payload, offset);

            throw new InvalidDataException("The transaction journal payload format is unknown.");
        }

        private static List<string> DeserializeRooted(string payload)
        {
            List<string> tableNames = new List<string>();
            XmlReaderSettings settings = new XmlReaderSettings();
#if NET35
            settings.ProhibitDtd = true;
#else
            settings.DtdProcessing = DtdProcessing.Prohibit;
#endif
#if !NETPORTABLE
            settings.XmlResolver = null;
#endif
            settings.IgnoreComments = true;
            settings.IgnoreWhitespace = true;

            try
            {
                using (StringReader stringReader = new StringReader(payload))
                using (XmlReader reader = XmlReader.Create(stringReader, settings))
                {
                    if (reader.MoveToContent() != XmlNodeType.Element ||
                        reader.LocalName != RootElement || reader.NamespaceURI.Length != 0)
                        throw new InvalidDataException("The transaction journal XML root is invalid.");
                    if (reader.IsEmptyElement)
                        throw new InvalidDataException("The transaction journal payload contains no table names.");

                    reader.ReadStartElement();
                    while (reader.MoveToContent() == XmlNodeType.Element)
                    {
                        if (reader.LocalName != ItemElement || reader.NamespaceURI.Length != 0 || reader.HasAttributes)
                            throw new InvalidDataException("The transaction journal XML contains an unexpected element.");

                        string tableName = reader.ReadElementContentAsString();
                        ValidateTableName(tableName);
                        tableNames.Add(tableName);
                    }

                    if (reader.NodeType != XmlNodeType.EndElement || reader.LocalName != RootElement)
                        throw new InvalidDataException("The transaction journal XML is truncated.");
                    reader.ReadEndElement();
                    if (reader.MoveToContent() != XmlNodeType.None)
                        throw new InvalidDataException("The transaction journal XML contains trailing data.");
                }
            }
            catch (InvalidDataException)
            {
                throw;
            }
            catch (Exception exception)
            {
                throw new InvalidDataException("The transaction journal XML is invalid.", exception);
            }

            ValidateResult(tableNames);
            return tableNames;
        }

        private static List<string> DeserializeLegacyFragment(string payload, int offset)
        {
            List<string> tableNames = new List<string>();
            while (offset < payload.Length)
            {
                if (!StartsWith(payload, offset, ItemStart))
                    throw new InvalidDataException("The legacy transaction journal payload contains unexpected data.");

                int valueOffset = offset + ItemStart.Length;
                int end = payload.IndexOf(ItemEnd, valueOffset, StringComparison.Ordinal);
                if (end < 0)
                    throw new InvalidDataException("The legacy transaction journal payload is truncated.");

                string tableName = payload.Substring(valueOffset, end - valueOffset);
                ValidateTableName(tableName);
                tableNames.Add(tableName);
                offset = SkipWhitespace(payload, end + ItemEnd.Length);
            }

            ValidateResult(tableNames);
            return tableNames;
        }

        private static void AppendEscapedXmlText(StringBuilder target, string value)
        {
            for (int i = 0; i < value.Length; i++)
            {
                char character = value[i];
                switch (character)
                {
                    case '&':
                        target.Append("&amp;");
                        break;
                    case '<':
                        target.Append("&lt;");
                        break;
                    case '>':
                        target.Append("&gt;");
                        break;
                    case '\r':
                        target.Append("&#xD;");
                        break;
                    case '\n':
                        target.Append("&#xA;");
                        break;
                    case '\t':
                        target.Append("&#x9;");
                        break;
                    default:
                        if (Char.IsHighSurrogate(character))
                        {
                            if (i + 1 >= value.Length || !Char.IsLowSurrogate(value[i + 1]))
                                throw new InvalidDataException("A table name contains an invalid Unicode surrogate.");
                            target.Append(character);
                            target.Append(value[++i]);
                        }
                        else
                        {
                            if (Char.IsLowSurrogate(character) ||
                                (character < 0x20) || character == 0xFFFE || character == 0xFFFF)
                                throw new InvalidDataException("A table name contains a character that is invalid in XML.");
                            target.Append(character);
                        }
                        break;
                }
            }
        }

        private static void ValidateResult(IList<string> tableNames)
        {
            if (tableNames.Count == 0)
                throw new InvalidDataException("The transaction journal payload contains no table names.");
        }

        private static void ValidateTableName(string tableName)
        {
            if (String.IsNullOrEmpty(tableName))
                throw new InvalidDataException("A transaction journal table name is empty.");
        }

        private static bool IsNullOrWhiteSpace(string value)
        {
            if (value == null)
                return true;

            for (int i = 0; i < value.Length; i++)
            {
                if (!Char.IsWhiteSpace(value[i]))
                    return false;
            }

            return true;
        }

        private static int SkipWhitespace(string value, int offset)
        {
            while (offset < value.Length && Char.IsWhiteSpace(value[offset]))
                offset++;
            return offset;
        }

        private static bool StartsWith(string value, int offset, string expected)
        {
            return offset <= value.Length - expected.Length &&
                String.CompareOrdinal(value, offset, expected, 0, expected.Length) == 0;
        }
    }
}
