﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace RoboNuGet.Data
{
    internal class MsBuild
    {
        public string Target { get; set; }

        public bool NoLogo { get; set; }

        public Dictionary<string, string> Properties { get; set; }

        public Dictionary<string, string> Switches { get; set; } = new Dictionary<string, string>();

        [JsonIgnore]
        public string ProjectFile { get; set; }

        public override string ToString()
        {
            var arguments = new List<string>();

            if (!string.IsNullOrEmpty(Target))
            {
                arguments.Add($"/target:{Target}");
            }

            if (NoLogo)
            {
                arguments.Add("/nologo");
            }

            arguments.AddRange(Switches.Select(x => $"/{x.Key}{(string.IsNullOrEmpty(x.Value) ? string.Empty : $":{x.Value}")}"));
            arguments.AddRange(Properties.Select(property => $"/property:{property.Key}=\"{property.Value}\""));

            arguments.Add(ProjectFile);

            return string.Join(" ", arguments);
        }

        public static implicit operator string(MsBuild msBuild)
        {
            return msBuild.ToString();
        }
    }
}