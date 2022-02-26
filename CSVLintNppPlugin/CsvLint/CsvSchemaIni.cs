﻿using System;
using System.Collections.Generic;
using System.IO;

namespace CSVLintNppPlugin.CsvLint
{
    class CsvSchemaIni
    {
        public static Dictionary<string, string> ReadIniSection(string filePath)
        {
            // file name and path of schema.ini
            var path = Path.GetDirectoryName(filePath);
            var file = Path.GetFileName(filePath);

            // schema.ini and section name
            var inifile = Path.Combine(path, "schema.ini");
            var section = string.Format("[{0}]", file.ToLower());

            // read entire ini file and look for section
            var inilines = new Dictionary<string, string>();

            // not a new file that hasn't been saved yet
            if ( (path != "") && File.Exists(inifile) )
            {
                using (StreamReader reader = new StreamReader(inifile))
                {
                    string line;
                    bool bSec = false;
                    while ((line = reader.ReadLine()) != null)
                    {
                        if (string.IsNullOrEmpty(line)) continue;
                        // check section
                        if (line[0] == '[')
                        {
                            // check current section and inilines index
                            bSec = line.ToLower() == section;
                        }
                        else if (bSec)
                        {
                            var spl = line.Split('=');
                            var key = line;
                            var val = "";
                            if (spl.Length > 1)
                            {
                                key = spl[0];
                                val = spl[1];
                            }

                            inilines.Add(key, val);
                        }
                    }
                }
            }

            return inilines;
        }

        public static bool WriteIniSection(string FilePath, string inikeys, out string errmsg)
        {
            // file name and path of schema.ini
            var path = Path.GetDirectoryName(FilePath);
            var file = Path.GetFileName(FilePath);
            errmsg = "";

            // schema.ini and section name
            var inifile = string.Format("{0}\\{1}", path, "schema.ini");
            var section = string.Format("[{0}]", file.ToLower());

            // read entire ini file and look for section
            var inilines = new List<string>();
            int idx = -1;

            // check if schema.ini exists
            if (File.Exists(inifile))
            {
                using (StreamReader reader = new StreamReader(inifile))
                {
                    string line;
                    bool bSec = false;
                    while ((line = reader.ReadLine()) != null)
                    {
                        if (line != "")
                        {
                            // check section
                            if (line[0] == '[')
                            {
                                // check current section and inilines index
                                bSec = line.ToLower() == section;
                                if (bSec) idx = inilines.Count;
                            }
                        }

                        // any other lines; just copy verbatim
                        if (!bSec) inilines.Add(line);
                    }
                }
            }

            // append any new sections at end
            if (idx == -1)
            {
                if ((inilines.Count > 0) && (inilines[inilines.Count - 1] != "")) inilines.Add("");
                idx = inilines.Count;
            }

            // section header
            inilines.Insert(idx + 0, string.Format("[{0}]", file));
            inilines.Insert(idx + 1, inikeys);
            //inilines.Insert(idx + 2, ""); // add one more empty line to separate next section

            // overwrite schema.ini file; `using` will implicitly do .Close() .Dispose() at the end
            try
            {
                using (StreamWriter writer = new StreamWriter(inifile, false))
                {
                    foreach (var str in inilines)
                    {
                        writer.WriteLine(str);
                    }
                    writer.Close();
                }
            }
            catch (Exception e)
            {
                // error
                errmsg = e.Message;
                return false;
            }

            // ok
            return true;
        }
    }
}
