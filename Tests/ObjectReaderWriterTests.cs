/*
  Copyright (c) 2012 Ant Micro <www.antmicro.com>

  Authors:
   * Konrad Kruczynski (kkruczynski@antmicro.com)
   * Piotr Zierhoffer (pzierhoffer@antmicro.com)

  Permission is hereby granted, free of charge, to any person obtaining
  a copy of this software and associated documentation files (the
  "Software"), to deal in the Software without restriction, including
  without limitation the rights to use, copy, modify, merge, publish,
  distribute, sublicense, and/or sell copies of the Software, and to
  permit persons to whom the Software is furnished to do so, subject to
  the following conditions:

  The above copyright notice and this permission notice shall be
  included in all copies or substantial portions of the Software.

  THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
  EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
  MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
  NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE
  LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION
  OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION
  WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
*/
using System;
using System.Linq;
using NUnit.Framework;
using System.IO;
using System.Collections.Generic;

namespace AntMicro.Migrant.Tests
{
	[TestFixture]
	public class ObjectReaderWriterTests
	{
		[Test]
		public void ShouldHandleTwoWritesAndReads()
		{
			var strings = new [] { "One", "Two" };

			var typeIndices = new List<Type>
			{
				typeof(string)
			};
			var stream = new MemoryStream();
			var writer = new ObjectWriter(stream, typeIndices);
			writer.WriteObject(strings[0]);
			writer.WriteObject(strings[1]);
			var position = stream.Position;

			stream.Seek(0, SeekOrigin.Begin);
			var types = typeIndices.ToArray();
			var reader = new ObjectReader(stream, types, false);
			Assert.AreEqual(strings[0], reader.ReadObject<string>());
			Assert.AreEqual(strings[1], reader.ReadObject<string>());
			Assert.AreEqual(position, stream.Position);
		}
	}
}

