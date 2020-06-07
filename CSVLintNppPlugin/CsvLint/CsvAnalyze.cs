﻿// -------------------------------------
// CsvAnalyze
// Analyze csv data return a CsvDefinition,
// infer settings, dateformat, columns, widths etc. from input data,
// -------------------------------------
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CSVLint.Tools;

namespace CSVLint
{
    class CsvAnalyze
    {
        private class CsvColumStats
        {
            public string Name = "";
            public int MinWidth = 9999;
            public int MaxWidth = 0;
            public int CountString = 0;
            public int CountInteger = 0;
            public int CountDecimal = 0;
            public int CountDecimalComma = 0;
            public int CountDecimalPoint = 0;
            public int DecimalDigMax = 0; // maximum digits, example "1234.5" = 4 digits
            public int DecimalDecMax = 0; // maximum decimals, example "123.45" = 2 decimals
            public int CountDateTime = 0;
            public char DateSep = '\0';
            public int DateMax1 = 0;
            public int DateMax2 = 0;
            public int DateMax3 = 0;
        }

        /// <summary>
        /// Infer CSV definition from data; determine separators, column names, datatypes etc
        /// </summary>
        /// <param name="data"></param>
        /// <returns></returns>
        public static CsvDefinition InferFromData(string data)
        {
            // First do a letter frequency analysis on each row
            var s = new StringReader(data);
            string line;
            int lineCount = 0, linesQuoted = 0;

            // statistics about this data
            var frequencies = new List<Dictionary<char, int>>();        // frequencies of letters per line
            var occurrences = new Dictionary<char, int>();              // occurences of letters in entire dataset
            var frequenciesQuoted = new List<Dictionary<char, int>>();  // frequencies of quoted letters per line
            var occurrencesQuoted = new Dictionary<char, int>();        // occurences of quoted letters in entire dataset
            var bigSpaces = new Dictionary<int, int>();                 // fixed width data; more than 1 spaces
            var wordStarts = new Dictionary<int, int>();                // fixed width data; places after space where characters starts again, or switch from numeric to alphacharacter
            var inQuotes = false;
            var letterFrequencyQuoted = new Dictionary<char, int>();
            var lineLengths = new Dictionary<int, int>();

            // analyse individual character frequencies
            while ((line = s.ReadLine()) != null)
            {
                // letter freq per line
                var letterFrequency = new Dictionary<char, int>();

                // line length
                lineLengths.Increase(line.Length);

                // process characters in this line
                int spaces = 0, i = 0, num = -1;
                foreach (var c in line)
                {
                    letterFrequency.Increase(c);
                    occurrences.Increase(c);

                    if (c == '"') inQuotes = !inQuotes;
                    else if (!inQuotes)
                    {
                        letterFrequencyQuoted.Increase(c);
                        occurrencesQuoted.Increase(c);
                    }

                    // check fixed columns
                    int newcol = 0;
                    if (c == ' ')
                    {
                        // more than 2 spaces could indicate new column
                        if (++spaces > 1) bigSpaces.Increase((i + 1));

                        // one single space after a digit might be a new column
                        if (num == 1)
                        {
                            wordStarts.Increase(i);
                            num = 0;
                        }
                    }
                    else
                    {
                        // more than 2 spaces could indicate new column
                        if (spaces > 1) newcol = 1;
                        spaces = 0;

                        // switch between alpha and numeric characters could indicate new column
                        int checknum = ("0123456789".IndexOf(c));
                        // ignore characters that can be both numeric or alpha values example "A.B." or "Smith-Johnson"
                        int ignore = (".-+".IndexOf(c));
                        if (ignore < 0)
                        {
                            if (checknum < 0)
                            {
                                if (num == 1) newcol = 1;
                                num = 0;
                            }
                            else
                            {
                                if (num == 0) newcol = 1;
                                num = 1;
                            };
                        };
                        // new column found
                        if (newcol == 1) wordStarts.Increase(i);
                    }

                    // next character
                    i++;
                }

                frequencies.Add(letterFrequency);
                if (!inQuotes)
                {
                    frequenciesQuoted.Add(letterFrequencyQuoted);
                    letterFrequencyQuoted = new Dictionary<char, int>();
                    linesQuoted++;
                }

                // stop after 20 lines
                if (lineCount++ > 20) break;
            }

            // check the variance on the frequency of each char
            var variances = new Dictionary<char, float>();
            foreach (var c in occurrences.Keys)
            {
                var mean = (float)occurrences[c] / lineCount;
                float variance = 0;
                foreach (var frequency in frequencies)
                {
                    var f = 0;
                    if (frequency.ContainsKey(c)) f = frequency[c];
                    variance += (f - mean) * (f - mean);
                }
                variance /= lineCount;
                variances.Add(c, variance);
            }

            // check variance on frequency of quoted chars(?)
            var variancesQuoted = new Dictionary<char, float>();
            foreach (var c in occurrencesQuoted.Keys)
            {
                var mean = (float)occurrencesQuoted[c] / linesQuoted;
                float variance = 0;
                foreach (var frequency in frequenciesQuoted)
                {
                    var f = 0;
                    if (frequency.ContainsKey(c)) f = frequency[c];
                    variance += (f - mean) * (f - mean);
                }
                variance /= lineCount;
                variancesQuoted.Add(c, variance);
            }

            // get separator
            char Separator = GetSeparatorFromVariance(variances, occurrences, lineCount, out var uncertancy);

            // The char with lowest variance is most likely the separator
            CsvDefinition result = new CsvDefinition(Separator);

            //var Separator = GetSeparatorFromVariance(variances, occurrences, lineCount, out var uncertancy);
            var separatorQuoted = GetSeparatorFromVariance(variancesQuoted, occurrencesQuoted, linesQuoted, out var uncertancyQuoted);
            if (uncertancyQuoted < uncertancy)
                result.Separator = separatorQuoted;
            else if (uncertancy < uncertancyQuoted || (uncertancy == uncertancyQuoted && lineCount > linesQuoted)) // It was better ignoring quotes!
                result.TextQualifier = '\0';

            // head column name
            result.ColNameHeader = (result.Separator != '\0');

            // Exception, probably not tabular data file
            if ( (result.Separator == '\0') && (lineLengths.Count > 1) )
            {
                // check for typical XML characters
                var xml1 = (occurrences.ContainsKey('>') ? occurrences['>'] : 0);
                var xml2 = (occurrences.ContainsKey('<') ? occurrences['<'] : 0);

                // check for binary characters, chr(31) or lower and not TAB
                var bin = occurrences.Where(x => (int)x.Key < 32 && (int)x.Key != 9).Sum(x => x.Value);

                // set filetype as first column name, as a hint to user
                var guess = "Textfile";
                if (bin > 0) guess = "Binary";
                if ((xml1 > 0) && (xml1 == xml2)) guess = "XML";

                // add single column and bail!
                result.AddColumn(guess, 9999, ColumnType.String, "");
                return result;
            }

            // Failed to detect separator, could it be a fixed-width file?
            if (result.Separator == '\0')
            {
                // big spaces
                var commonSpace = bigSpaces.Where(x => x.Value == lineCount).Select(x => x.Key).OrderByDescending(x => x);
                var lastvalue = 0;
                int lastStart = 0;
                var foundfieldWidths = new List<int>();
                foreach (var space in commonSpace)
                {
                    if (space != lastvalue - 1)
                    {
                        foundfieldWidths.Add(space);
                        lastStart = space;
                    }
                    lastvalue = space;
                }

                // new columns or numeric/alpha 
                var commonBreaks = wordStarts.Where(x => x.Value == lineCount).Select(x => x.Key).OrderBy(x => x);

                //foundfieldWidths.AddRange(commonBreaks); // AddRange simply adds duplicates

                // only add unique breaks
                foreach (var br in commonBreaks)
                    if (!foundfieldWidths.Contains(br))
                        foundfieldWidths.Add(br);

                foundfieldWidths.Sort();


                if (foundfieldWidths.Count < 3) return result; // unlikely fixed width
                foundfieldWidths.Add(-1); // Last column gets "the rest"
                result.FieldWidths = foundfieldWidths;
            }

            // reset string reader to first line is not possible, create a new one
            s = new StringReader(data);

            // examine data and keep statistics for each column
            List<CsvColumStats> colstats = new List<CsvColumStats>();
            lineCount = 0;

            // examine data
            while ((line = s.ReadLine()) != null)
            {
                // keep track of how many lines
                lineCount++;

                // get values from line
                List<string> values = new List<string>();
                if (result.Separator == '\0')
                {
                    // fixed width columns
                    int pos1 = 0;
                    for (int i = 0; i < result.FieldWidths.Count(); i++)
                    {
                        // if line is too short, columns missing?
                        if (pos1 > line.Length) break;

                        // next column end pos, last column gets the rest
                        int pos2 = result.FieldWidths[i];
                        if (pos2 < 0) pos2 = line.Length;

                        // get column value
                        string val = line.Substring(pos1, pos2 - pos1);
                        values.Add(val);
                        pos1 = pos2;
                    }
                }
                else
                {
                    // delimited columns
                    values = line.Split(result.Separator).ToList();
                }

                // inspect all values
                for (int i = 0; i < values.Count(); i++)
                {
                    // add columnstats if needed
                    if (i > colstats.Count()-1) colstats.Add(new CsvColumStats());

                    // next value to evaluate
                    string val = values[i].Trim();

                    // adjust for quoted values
                    if (val[0] == '"')
                    {
                        val = val.Trim('"');
                    }

                    // assume first line only contains column header names
                    if (lineCount == 1)
                    {
                        colstats[i].Name = (result.ColNameHeader ? val : "F"+(i+1) );
                    }

                    // data lines
                    if ((lineCount > 1) || (!result.ColNameHeader))
                        {
                        int length = val.Count();

                        // only consider non-empty values, ignore empty strings
                        if (length > 0) {

                            // keep minimum width
                            if (length < colstats[i].MinWidth) colstats[i].MinWidth = length;

                            // keep maximum width
                            if (length > colstats[i].MaxWidth) colstats[i].MaxWidth = length;

                            // check each character in string
                            int digits = 0;
                            int sign = 0;
                            int signpos = 0;
                            int point = 0;
                            int comma = 0;
                            int datesep = 0;
                            int other = 0;
                            char sep1 = '\0';
                            string datedig1 = "";
                            int ddmax = 0;
                            char dec = '\0';

                            int vallength = val.Length;
                            for (int charidx = 0; charidx < vallength; charidx++)
                            {
                                char ch = val[charidx];

                                if (ch >= '0' && ch <= '9')
                                {
                                    digits++;
                                }
                                else if (ch == '.')
                                {
                                    point++;
                                    dec = ch;
                                }
                                else if (ch == ',')
                                {
                                    comma++;
                                    dec = ch;
                                }
                                else if ("\\/-: ".IndexOf(ch) > 0)
                                {
                                    datesep++;
                                    if (sep1 == '\0')
                                    {
                                        // check if numeric up to the first separator
                                        sep1 = ch;
                                        datedig1 = val.Substring(0, val.IndexOf(ch));
                                        bool isNumeric = int.TryParse(datedig1, out int n);
                                        if (isNumeric)
                                        {
                                            if (ddmax < n) ddmax = n;
                                        }
                                    }
                                }
                                else
                                {
                                    other++;
                                };

                                // plus and minus are signs for digits, check separately because minus ('-') is also counted as sep
                                if (ch == '+' || ch == '-')
                                {
                                    sign++;
                                    signpos = charidx;
                                }
                            }

                            // date, examples "31-12-2019", "1/1/2019", "2019-12-31" etc.
                            if ((length >= 8) && (length <= 10) && (datesep == 2) && (other == 0))
                            {
                                colstats[i].CountDateTime++;
                                colstats[i].DateSep = sep1;
                                if (colstats[i].DateMax1 < ddmax) colstats[i].DateMax1 = ddmax;
                            }
                            // or datetime, examples "31-12-2019 23:59:00", "1/1/2019 12:00", "2019-12-31 23:59:59.000" etc.
                            else if ((length >= 13) && (length <= 23) && (datesep >= 2) && (datesep <= 6) && (other == 0))
                            {
                                colstats[i].CountDateTime++;
                                colstats[i].DateSep = sep1;
                                if (colstats[i].DateMax1 < ddmax) colstats[i].DateMax1 = ddmax;
                            }
                            else if ((digits > 0) && (point != 1) && (comma != 1) && (sign <= 1) && (signpos == 0) && (length <= 8) && (other == 0))
                            {
                                // numeric integer, examples "123", "-99", "+10" etc.
                                colstats[i].CountInteger++;
                            }
                            else if ((digits > 0) && ( (point == 1) || (comma == 1) ) && (sign <= 1) && (length <= 12) && (datesep == 0) && (other == 0))
                            {
                                // numeric integer, examples "12.3", "-99,9" etc.
                                colstats[i].CountDecimal++;
                                if (dec == '.') colstats[i].CountDecimalPoint++;
                                if (dec == ',') colstats[i].CountDecimalComma++;

                                // maximum decimal places, example "1234.567" = 4 digits and 3 decimals
                                int countdec = val.Length - val.LastIndexOf(dec) - 1;
                                int countdig = val.Length - countdec - 1;
                                if (countdec > colstats[i].DecimalDecMax) colstats[i].DecimalDecMax = countdec;
                                if (countdig > colstats[i].DecimalDigMax) colstats[i].DecimalDigMax = countdig;
                            }
                            else
                            {
                                // any other
                                colstats[i].CountString++;
                            };
                        }
                    }
                }
            }

            // add columns as actual fields
            int idx = 0;
            foreach (CsvColumStats stats in colstats)
            {
                string name = stats.Name;
                string mask = "";
                ColumnType typ = ColumnType.String;
                if ((stats.CountInteger > stats.CountString) && (stats.CountInteger > stats.CountDecimal) && (stats.CountInteger > stats.CountDateTime))
                {
                    typ = ColumnType.Integer;
                }
                else if ((stats.CountDecimal > stats.CountString) && (stats.CountDecimal > stats.CountInteger) && (stats.CountDecimal > stats.CountDateTime))
                {
                    typ = ColumnType.Decimal;
                    char dec = (stats.CountDecimalPoint > stats.CountDecimalComma ? '.' : ',');

                    // mask, example "9999.99"
                    mask = string.Format("{0}{1}{2}", mask.PadLeft(stats.DecimalDigMax, '9'), dec, mask.PadLeft(stats.DecimalDecMax, '9'));

                    // Note: when dataset contains values "12.345" and "1234.5" then maxlength=6
                    // However then DecimalDigMax=4 and DecimalDigMax=3 so mask is "9999.999" and maxlength should be 8 (not 6)
                    if (mask.Length < stats.MaxWidth)
                    {
                        stats.MaxWidth = mask.Length;
                    };

                    // keep global decimal point character
                    result.DecimalSymbol = dec;

                    // keep global nr of decimal places
                    if (result.NumberDigits < stats.DecimalDecMax) result.NumberDigits = stats.DecimalDecMax;
                }
                else if ((stats.CountDateTime > stats.CountString) && (stats.CountDateTime > stats.CountInteger) && (stats.CountDateTime > stats.CountDecimal))
                {
                    typ = ColumnType.DateTime;
                    // dateformat order, educated guess
                    var part1 = "MM";
                    var part2 = "dd";
                    var part3 = "yyyy";
                    // if the first digit higher than 12, then it cannot be a month
                    if ((stats.DateMax1 > 12) && (stats.DateMax1 <= 31))
                    {
                        part1 = "dd";
                        part2 = "MM";
                    }
                    // if the first digit higher than 1000, then probably year
                    if (stats.DateMax1 > 1000)
                    {
                        part1 = "yyyy";
                        part2 = "MM";
                        part3 = "dd";
                    }
                    // if first separator is ':' it's probably a time value example "01:23:45.678"
                    if (stats.DateSep == ':')
                    {
                        part1 = "HH";
                        part2 = "mm";
                        part3 = "ss";
                    }

                    // build mask
                    mask = string.Format("{0}{1}{2}{3}{4}", part1, stats.DateSep, part2, stats.DateSep, part3);

                    // build mask, fixed length date "dd-mm-yyyy" or not "d-m-yyyy"
                    if (stats.MinWidth < stats.MaxWidth)
                    {
                        mask = mask.Replace("dd", "d").Replace("MM", "M");
                    }

                    // single digit year, example "31-12-99"
                    if ((stats.MinWidth == stats.MaxWidth) && (stats.MinWidth == 8))
                    {
                        mask = mask.Replace("yyyy", "yy");
                    }

                    // also includes time
                    if (stats.MaxWidth >= 13) mask = mask + " HH:mm"; // example "01-01-2019 12:00"
                    if (stats.MaxWidth > 16) mask = mask + ":ss";    // example "1-1-2019 2:00:00"
                    if (stats.MaxWidth > 19) mask = mask + ".fff";   // example "01-01-2019 12:00:00.000"

                    // keep global datetime format
                    result.DateTimeFormat = mask;
                };

                // add column 
                result.AddColumn(idx, name, stats.MaxWidth, typ, mask);

                idx++;
            }

            // result
            return result;
        }

        private static char GetSeparatorFromVariance(Dictionary<char, float> variances, Dictionary<char, int> occurrences, int lineCount, out int uncertancy)
        {
            //var preferredSeparators = Main.Settings.Separators.Replace("\\t", "\t");
            var preferredSeparators = "\t,;|";
            uncertancy = 0;

            // The char with lowest variance is most likely the separator
            // Optimistic: check prefered with 0 variance 
            var separator = variances
                .Where(x => x.Value == 0f && preferredSeparators.IndexOf(x.Key) != -1)
                .OrderByDescending(x => occurrences[x.Key])
                .Select(x => (char?)x.Key)
                .FirstOrDefault();

            if (separator != null)
                return separator.Value;

            uncertancy++;
            var defaultKV = default(KeyValuePair<char, float>);

            // Ok, no perfect separator. Check if the best char that exists on all lines is a prefered separator
            var sortedVariances = variances.OrderBy(x => x.Value).ToList();
            var best = sortedVariances.FirstOrDefault(x => occurrences[x.Key] >= lineCount);
            if (!best.Equals(defaultKV) && preferredSeparators.IndexOf(best.Key) != -1)
                return best.Key;
            uncertancy++;

            // No? Second best?
            best = sortedVariances.Where(x => occurrences[x.Key] >= lineCount).Skip(1).FirstOrDefault();
            if (!best.Equals(defaultKV) && preferredSeparators.IndexOf(best.Key) != -1)
                return best.Key;
            uncertancy++;

            // Ok, screw the preferred separators, is any other char a perfect separator? (and common, i.e. at least 3 per line)
            separator = variances
                .Where(x => x.Value == 0f && occurrences[x.Key] >= lineCount * 2)
                .OrderByDescending(x => occurrences[x.Key])
                .Select(x => (char?)x.Key)
                .FirstOrDefault();
            if (separator != null)
                return separator.Value;

            uncertancy++;
            // Ok, I have no idea
            return '\0';
        }
    }
}
