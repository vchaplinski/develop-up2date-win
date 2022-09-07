﻿using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml;

namespace Up2dateService.SetupManager
{
    public class ChocoNugetInfo
    {
        private ChocoNugetInfo(string id, string title, string version, string publisher)
        {
            if (string.IsNullOrWhiteSpace(id))
                throw new ArgumentException($@"'{nameof(id)}' cannot be null or whitespace.", nameof(id));

            Id = id;
            Title = title;
            Version = version;
            Publisher = publisher;
        }

        public string Id { get; }
        public string Title { get; }
        public string Version { get; }
        public string Publisher { get; }

        public static ChocoNugetInfo GetInfo(string fullFilePath)
        {
            try
            {
                using (var zipFile = ZipFile.OpenRead(fullFilePath))
                {
                    var nuspec = zipFile.Entries.FirstOrDefault(zipArchiveEntry => zipArchiveEntry.Name.Contains(".nuspec"));
                    if (nuspec == null) return null;

                    using (var nuspecStream = nuspec.Open())
                    {
                        using (var sr = new StreamReader(nuspecStream, Encoding.UTF8))
                        {
                            var xmlData = sr.ReadToEnd();
                            var doc = new XmlDocument();
                            doc.LoadXml(xmlData);
                            var id = doc.GetElementsByTagName("id").Count > 0
                                ? doc.GetElementsByTagName("id")[0].InnerText
                                : null;
                            var title = doc.GetElementsByTagName("title").Count > 0
                                ? doc.GetElementsByTagName("title")[0].InnerText
                                : null;
                            var version = doc.GetElementsByTagName("version").Count > 0
                                ? doc.GetElementsByTagName("version")[0].InnerText
                                : null;
                            var publisher = doc.GetElementsByTagName("authors").Count > 0
                                ? doc.GetElementsByTagName("authors")[0].InnerText
                                : null;
                            return new ChocoNugetInfo(id, title, version, publisher);
                        }
                    }
                }
            }
            catch
            {
                return null;
            }
        }
    }
}