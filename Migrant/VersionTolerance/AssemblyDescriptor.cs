﻿// *******************************************************************
//
//  Copyright (c) 2012-2015, Antmicro Ltd <antmicro.com>
//
// Permission is hereby granted, free of charge, to any person obtaining
// a copy of this software and associated documentation files (the
// "Software"), to deal in the Software without restriction, including
// without limitation the rights to use, copy, modify, merge, publish,
// distribute, sublicense, and/or sell copies of the Software, and to
// permit persons to whom the Software is furnished to do so, subject to
// the following conditions:
//
// The above copyright notice and this permission notice shall be
// included in all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
// EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
// MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
// NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE
// LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION
// OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION
// WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
//
// *******************************************************************
using System;
using System.Reflection;
using System.Linq;
using System.Globalization;

namespace Antmicro.Migrant.VersionTolerance
{
    public class AssemblyDescriptor
    {
        public AssemblyDescriptor()
        {
        }

        public AssemblyDescriptor(Assembly assembly)
        {
            if(assembly.Modules.Count() != 1)
            {
                throw new ArgumentException("Multimoduled assemblies are not supported yet.");
            }

            this.assembly = assembly;

            Name = assembly.GetName().Name;
            Version = assembly.GetName().Version;
            Culture = assembly.GetName().CultureInfo;
            Token = assembly.GetName().GetPublicKeyToken();

            ModuleGUID = assembly.Modules.First().ModuleVersionId;
        }

        public void WriteTo(ObjectWriter writer)
        {
            writer.PrimitiveWriter.Write(Name);
            writer.PrimitiveWriter.Write(Version);
            writer.PrimitiveWriter.Write(Culture.Name);
            writer.PrimitiveWriter.Write((byte)Token.Length);
            writer.PrimitiveWriter.Write(Token);

            writer.PrimitiveWriter.Write(ModuleGUID);
        }

        public void ReadFrom(ObjectReader reader)
        {
            Name = reader.PrimitiveReader.ReadString();
            Version = reader.PrimitiveReader.ReadVersion();
            Culture = CultureInfo.GetCultureInfo(reader.PrimitiveReader.ReadString());
            var tokenLength = reader.PrimitiveReader.ReadByte();
            switch(tokenLength)
            {
            case 0:
                Token = new byte[0];
                break;
            case 8:
                Token = reader.PrimitiveReader.ReadBytes(8);
                break;
            default:
                throw new ArgumentException("Wrong token length!");
            }

            ModuleGUID = reader.PrimitiveReader.ReadGuid();
        }

        public Assembly Resolve()
        {
            if(assembly == null)
            {
                var assemblyName = new AssemblyName(FullName);
                assembly = Assembly.Load(assemblyName);
            }

            return assembly;
        }

        public Guid ModuleGUID { get; private set; } 

        // TODO: fix problem with culture name
        public string FullName { get { return string.Format("{0}, Version={1}, Culture={2}, PublicKeyToken={3}", Name, Version, "neutral"/*Culture.Name*/, Token.Length == 0 ? "null" : String.Join(string.Empty, Token.Select(x => string.Format("{0:x2}", x)))); } }

        public string Name { get; private set; }
        public Version Version { get; private set; }
        public CultureInfo Culture { get; private set; }
        public byte[] Token { get; private set; }

        private Assembly assembly;
    }

    internal static class PrimitiveWriterReaderExtensions
    {
        public static void Write(this PrimitiveWriter @this, Version version)
        {
            @this.Write(version.Major);
            @this.Write(version.Minor);
            @this.Write(version.Build);
            @this.Write(version.Revision);
        }
       
        public static Version ReadVersion(this PrimitiveReader @this)
        {
            var major = @this.ReadInt32();
            var minor = @this.ReadInt32();
            var build = @this.ReadInt32();
            var revision = @this.ReadInt32();

            if(revision != -1)
            {
                return new Version(major, minor, build, revision);
            }
            else if(build != -1)
            {
                return new Version(major, minor, build);
            }
            return new Version(major, minor);
        }
    }
}

