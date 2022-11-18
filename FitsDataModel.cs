// FITS is a fairly simple format.
// The file consists of chunks of 2880 bytes in size.
// Each section of the file is preceded by a header that contains a set of parameters in the form of ANSI text. Behind the header are data (binary or text).
// Header can residue 1 on more chunks and must end by 'END' keyword.
// The size of the data is obtained from the header (by BITPIX, NAXIS, etc.)
// The standard("simple") format can contain only images, text or binary tables.
// The format does not provide any hierarchical structures (trees, graphs). Only arrays, only hardcore.

// This implementation supports only Primary records and standard extensions (IMAGE, TABLE and BINTABLE).

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using System.Text.RegularExpressions;
using System.Security.Policy;
using System.Globalization;

namespace FITSDominator
{
    class FitsDataModel
    {
        const long BlockSize = 2880;     // p16. FITS files shall be interpreted as a sequence of 1 or more 2880-byte FITS blocks

        public List<FitsEntry> entries = new List<FitsEntry>();

        public FitsDataModel(byte [] data)
        {
            if (data.Length < BlockSize)
                return;

            long offset = 0;

            while (offset < data.Length)
            {
                FitsEntry entry = new FitsEntry();
                ParseEntry(data, offset, entry);
                offset += entry.headerSizeBytes + entry.data.Length;
                entries.Add(entry);
            }
        }

        private void ParseEntry (byte [] data, long offset, FitsEntry entry)
        {
            // Very first entry is Primary, other are distinguished by XTENSION keyword.

            if (offset == 0)
            {
                entry.type = FitsEntryType.Primary;
            }

            entry.entryOffsetBytes = offset;

            // Parse keywords until END (or end of file)

            while (offset < data.Length)
            {
                char[] paramRaw = new char[80];

                for (long i =0; i<paramRaw.Length; i++)
                {
                    paramRaw[i] = (char)data[offset + i];
                }

                string paramAsStr = new string(paramRaw);
                FitsParam param = new FitsParam(paramAsStr);

                entry.header.Add(param);
                if (param.name == "END")
                    break;

                offset += paramRaw.Length;
            }

            // Align offset to next block

            offset = NextBlock(offset);
            entry.headerSizeBytes = offset - entry.entryOffsetBytes;

            // Deduce entry type.

            if (entry.ParamExists("XTENSION") && entry.type == FitsEntryType.Unknown)
            {
                switch (entry.GetParam<string>("XTENSION"))
                {
                    case "IMAGE":
                        entry.type = FitsEntryType.ImageExtension;
                        break;
                    case "TABLE":
                        entry.type = FitsEntryType.AsciiTableExtension;
                        break;
                    case "BINTABLE":
                        entry.type = FitsEntryType.BinaryTableExtension;
                        break;
                }
            }

            // Deduce data size.

            // If entry type is unknown - skip until next XTENSION keyword (or end of file)

            long dataSize = 0;

            if (entry.type == FitsEntryType.Unknown)
            {
                while (offset < data.Length)
                {
                    string subString = "";
                    for (int i=0; i<8; i++)
                    {
                        subString += (char)data[offset + i];
                    }
                    if (subString == "XTENSION")
                        break;
                    offset += BlockSize;
                }

                if (offset >= data.Length)
                {
                    dataSize = data.Length - (entry.entryOffsetBytes + entry.headerSizeBytes);
                }
                else
                {
                    dataSize = offset - (entry.entryOffsetBytes + entry.headerSizeBytes);
                }
            }
            else
            {
                if (!entry.ParamExists("BITPIX"))
                    throw new Exception("Missing BITPIX keyword!");
                if (!entry.ParamExists("NAXIS"))
                    throw new Exception("Missing NAXIS keyword!");

                long elementSize = entry.GetParam<long>("BITPIX") / 8;
                long naxis = entry.GetParam<long>("NAXIS");
                long numElements = naxis != 0 ? 1 : 0;
                for (var i = 0; i < naxis; i++)
                {
                    string paramName = "NAXIS" + (i + 1).ToString();
                    if (!entry.ParamExists(paramName))
                        throw new Exception("Missing " + paramName + " keyword!");
                    numElements *= entry.GetParam<long>(paramName);
                }
                dataSize = numElements * elementSize;
            }
            
            // Round up to block size
            if ((dataSize % BlockSize) != 0)
            {
                dataSize = NextBlock(dataSize);
            }

            // Copy away data[]

            entry.data = new byte[dataSize];

            for (long i=0; i<dataSize; i++)
            {
                entry.data[i] = data[offset + i];
            }
        }

        private long NextBlock(long offset)
        {
            long blockNum = offset / BlockSize;
            return (blockNum + 1) * BlockSize;
        }

        public void Dump()
        {
            foreach (var entry in entries)
            {
                Console.WriteLine("FITS Entry: " + entry.type.ToString());
                Console.WriteLine("Header:");

                // Header

                foreach (var param in entry.header)
                {
                    if (param.comment != null)
                    {
                        Console.WriteLine("{0} = {1} // {2}", param.name, param.valueAsStr, param.comment);
                    }
                    else
                    {
                        Console.WriteLine("{0} = {1}", param.name, param.valueAsStr);
                    }
                }

                // Body

                Console.WriteLine("Data: {0} bytes", entry.data.Length);

                Console.WriteLine(" ");
            }
        }

        public FitsEntry GetPrimary()
        {
            foreach(var entry in entries)
            {
                if (entry.type == FitsEntryType.Primary)
                {
                    return entry;
                }
            }
            return null;
        }

    }

    public enum FitsEntryType
    {
        Unknown = -1,
        Primary,
        ImageExtension,
        AsciiTableExtension,
        BinaryTableExtension,
    }

    class FitsEntry
    {
        public FitsEntryType type = FitsEntryType.Unknown;
        public List<FitsParam> header = new List<FitsParam>();
        public long entryOffsetBytes = 0;
        public long headerSizeBytes = 0;
        public byte[] data = null;

        public T GetParam<T>(string name)
        {
            foreach (var param in header)
            {
                if (param.name == name)
                {
                    return (T)param.value;
                }
            }
            return default;
        }

        public bool ParamExists(string name)
        {
            foreach (var param in header)
            {
                if (param.name == name)
                {
                    return true;
                }
            }
            return false;
        }
    }

    class FitsParam
    {
        public string name;
        public string valueAsStr;
        public object value;
        public string comment;

        public FitsParam(string text)
        {
            // http://regexstorm.net/

            // End

            if (text.Trim() == "END")
            {
                name = "END";
                valueAsStr = "";
                value = null;
                comment = null;
                return;
            }

            // Comments
            // (?<key>(COMMENT|HISTORY))[\s]*(?<comment>.+){0,}

            Regex regex = new Regex(@"(?<key>(COMMENT|HISTORY))[\s]*(?<comment>.+){0,}");

            var matches = regex.Matches(text);

            if (matches.Count != 0)
            {
                name = matches[0].Groups["key"].Value.Trim();
                valueAsStr = "";
                value = null;
                comment = matches[0].Groups["comment"].Value.Trim();
                return;
            }

            // Version 1:
            // (?<key>[\w]+)[\s]*=[\s]*(?<value>([\w.eE+-]+|\'.+\'|\'\'|\s+))[\s]*(?<comment>/.+){0,}

            regex = new Regex(@"(?<key>[\w]+)[\s]*=[\s]*(?<value>([\w.eE+-]+|\'.+\'|\'\'|\s+))[\s]*(?<comment>/.+){0,}");

            matches = regex.Matches(text);

            if (matches.Count == 0)
            {
                // Skip garbage
                name = "GARBAGE";
                valueAsStr = "";
                value = null;
                comment = text;
                return;
            }

            name = matches[0].Groups["key"].Value.Trim();
            if (matches[0].Groups["comment"].Value != "")
            {
                comment = matches[0].Groups["comment"].Value.Substring(1).Trim();
            }
            valueAsStr = matches[0].Groups["value"].Value.Trim();

            if (valueAsStr != "")
            {
                if (valueAsStr == "T" || valueAsStr == "F")
                    value = ParseBool(valueAsStr);
                else if (valueAsStr.Contains('\''))
                    value = ParseStr(valueAsStr);
                else if (valueAsStr.Contains('.'))
                    value = ParseDouble(valueAsStr);
                else if (valueAsStr.Contains(',') && !valueAsStr.Contains('.'))
                    value = ParseComplexInt(valueAsStr);
                else if (valueAsStr.Contains(',') && valueAsStr.Contains('.'))
                    value = ParseComplexDouble(valueAsStr);
                else
                    value = ParseInt(valueAsStr);
            }
        }

        private bool ParseBool (string str)
        {
            return (str[0] == 'T');
        }

        private string ParseStr(string str)
        {
            return str.Trim('\'');
        }

        private long ParseInt (string str)
        {
            return long.Parse(str);
        }

        private double ParseDouble(string str)
        {
            return double.Parse(str, CultureInfo.InvariantCulture);
        }

        private Tuple<long,long> ParseComplexInt(string str)
        {
            string[] parts = str.Split(',');
            parts[0] = parts[0].Replace('(', ' ');
            parts[1] = parts[1].Replace(')', ' ');
            return new Tuple<long, long>(long.Parse(parts[0]), long.Parse(parts[1]));
        }

        private Tuple<double, double> ParseComplexDouble(string str)
        {
            string[] parts = str.Split(',');
            parts[0] = parts[0].Replace('(', ' ').Trim();
            parts[1] = parts[1].Replace(')', ' ').Trim();
            return new Tuple<double, double>(
                double.Parse(parts[0], CultureInfo.InvariantCulture), 
                double.Parse(parts[1], CultureInfo.InvariantCulture));
        }
    }

}
