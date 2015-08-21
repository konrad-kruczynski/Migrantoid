/*
  Copyright (c) 2012 Ant Micro <www.antmicro.com>

  Authors:
   * Konrad Kruczynski (kkruczynski@antmicro.com)
   * Piotr Zierhoffer (pzierhoffer@antmicro.com)
   * Mateusz Holenko (mholenko@antmicro.com)

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
using System.Collections.Generic;
using System.IO;
using Antmicro.Migrant.Customization;
using System.Reflection.Emit;
using Antmicro.Migrant.Utilities;
using Antmicro.Migrant.BultinSurrogates;
using Antmicro.Migrant.VersionTolerance;

namespace Antmicro.Migrant
{
    /// <summary>
    /// Provides the mechanism for binary serialization and deserialization of objects.
    /// </summary>
    /// <remarks>
    /// Please consult the general serializer documentation to find the limitations
    /// and constraints which serialized objects must fullfill.
    /// </remarks>
    public class Serializer
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="Antmicro.Migrant.Serializer"/> class.
        /// </summary>
        /// <param name='settings'>
        /// Serializer's settings, can be null or not given, in that case default settings are
        /// used.
        /// </param>
        public Serializer(Settings settings = null)
        {
            if(settings == null)
            {
                settings = new Settings(); // default settings
            }
            this.settings = settings;
            writeMethodCache = new Dictionary<Type, Action<ObjectWriter, PrimitiveWriter, object>>();
            objectsForSurrogates = new SwapList();
            surrogatesForObjects = new SwapList();
            readMethodCache = new Dictionary<Type, Func<ObjectReader, int, object>>();

            if(settings.SupportForISerializable)
            {
                ForObject<System.Runtime.Serialization.ISerializable>().SetSurrogate(x => new SurrogateForISerializable(x));
                ForSurrogate<SurrogateForISerializable>().SetObject(x => x.Restore());
                ForObject<Delegate>().SetSurrogate<Func<Delegate, object>>(null); //because Delegate implements ISerializable but we support it directly.
            }

            if(settings.SupportForIXmlSerializable)
            {
                ForObject<System.Xml.Serialization.IXmlSerializable>().SetSurrogate(x => new SurrogateForIXmlSerializable(x));
                ForSurrogate<SurrogateForIXmlSerializable>().SetObject(x => x.Restore());
            }
        }

        /// <summary>
        /// Serializes the specified object to a given stream.
        /// </summary>
        /// <param name='obj'>
        /// Object to serialize along with its references.
        /// </param>
        /// <param name='stream'>
        /// Stream to which the given object should be serialized. Has to be writeable.
        /// </param>
        public void Serialize(object obj, Stream stream)
        {
            WriteHeader(stream);
            using(var writer = ObtainWriter(stream))
            {
                writer.WriteObject(obj);
            }
            serializationDone = true;
        }

        /// <summary>
        /// Returns the open stream serializer, which can be used to do consecutive serializations
        /// </summary>
        /// <returns>The open stream serializer.</returns>
        /// <param name="stream">Stream.</param>
        public OpenStreamSerializer ObtainOpenStreamSerializer(Stream stream)
        {
            WriteHeader(stream);
            serializationDone = true;
            return new OpenStreamSerializer(ObtainWriter(stream));
        }

        /// <summary>
        /// Returns the open stream serializer, which can be used to do consecutive deserializations when
        /// same technique was used to serialize data.
        /// </summary>
        /// <returns>The open stream deserializer.</returns>
        /// <param name="stream">Stream.</param>
        public OpenStreamDeserializer ObtainOpenStreamDeserializer(Stream stream)
        {
            bool preserveReferences;
            ThrowOnWrongResult(TryReadHeader(stream, out preserveReferences));
            if(!settings.UseBuffering)
            {
                stream = new PeekableStream(stream);
            }
            return new OpenStreamDeserializer(ObtainReader(stream, preserveReferences), settings, stream);
        }

        /// <summary>
        /// Gives the ability to set callback providing object for surrogate of given type. The object will be provided instead of such
        /// surrogate in the effect of deserialization.
        /// </summary>
        /// <returns>
        /// Object letting you set the object for the given surrogate type.
        /// </returns>
        /// <typeparam name='TSurrogate'>
        /// The type for which callback will be invoked.
        /// </typeparam>
        public ObjectForSurrogateSetter<TSurrogate> ForSurrogate<TSurrogate>()
        {
            return new ObjectForSurrogateSetter<TSurrogate>(this);
        }

        /// <summary>
        /// Gives the ability to set callback providing object for surrogate of given type. The object will be provided instead of such
        /// surrogate in the effect of deserialization.
        /// </summary>
        /// <returns>
        /// Object letting you set the object for the given surrogate type.
        /// </returns>
        /// <param name="type">
        /// The type for which callback will be invoked.
        /// </param>
        public ObjectForSurrogateSetter ForSurrogate(Type type)
        {
            return new ObjectForSurrogateSetter(this, type);
        }

        /// <summary>
        /// Gives the ability to set callback providing surrogate for objects of given type. The surrogate will be serialized instead of 
        /// the object of that type.
        /// </summary>
        /// <returns>
        /// Object letting you set the surrogate for the given type.
        /// </returns>
        /// <typeparam name='TObject'>
        /// The type for which callback will be invoked.
        /// </typeparam>
        public SurrogateForObjectSetter<TObject> ForObject<TObject>()
        {
            return new SurrogateForObjectSetter<TObject>(this);
        }

        /// <summary>
        /// Gives the ability to set callback providing surrogate for objects of given type. The surrogate will be serialized instead of 
        /// the object of that type.
        /// </summary>
        /// <returns>
        /// Object letting you set the surrogate for the given type.
        /// </returns>
        /// <param name="type">
        /// The type for which callback will be invoked.
        /// </param>
        public SurrogateForObjectSetter ForObject(Type type)
        {
            return new SurrogateForObjectSetter(this, type);
        }

        /// <summary>
        /// Deserializes object from the specified stream.
        /// </summary>
        /// <param name='stream'>
        /// The stream to read data from. Must be readable.
        /// </param>
        /// <typeparam name='T'>
        /// The expected type of the deserialized object. The deserialized object must be
        /// convertible to this type.
        /// </typeparam>
        public T Deserialize<T>(Stream stream)
        {
            T result;
            ThrowOnWrongResult(TryDeserialize(stream, out result));
            return result;
        }

        /// <summary>
        /// Tries to deserialize object from specified stream.
        /// </summary>
        /// <returns>Operation result status.</returns>
        /// <param name="stream">
        /// The stream to read data from. Must be readable.
        /// </param>
        /// <param name="obj">Deserialized object.</param>
        /// <typeparam name="T">
        /// The expected type of the deserialized object. The deserialized object must be
        /// convertible to this type.
        /// </typeparam>
        public DeserializationResult TryDeserialize<T>(Stream stream, out T obj)
        {
            bool unused;
            obj = default(T);

            var headerResult = TryReadHeader(stream, out unused);
            if(headerResult != DeserializationResult.OK)
            {
                return headerResult;
            }

            using(var objectReader = ObtainReader(stream, unused))
            {
                try
                {
                    obj = objectReader.ReadObject<T>();
                    deserializationDone = true;
                    return DeserializationResult.OK;
                }
                catch(VersionToleranceException ex)
                {
                    lastException = ex;
                    return DeserializationResult.TypeStructureChanged;
                }
                catch(Exception ex)
                {
                    lastException = ex;
                    return DeserializationResult.StreamCorrupted;
                }
            }
        }

        /// <summary>
        /// Is invoked before serialization, once for every unique, serialized object. Provides this
        /// object in its single parameter.
        /// </summary>
        public event Action<object> OnPreSerialization;

        /// <summary>
        /// Is invoked after serialization, once for every unique, serialized object. Provides this
        /// object in its single parameter.
        /// </summary>
        public event Action<object> OnPostSerialization;

        /// <summary>
        /// Is invoked before deserialization, once for every unique, serialized object. Provides this
        /// object in its single parameter.
        /// </summary>
        public event Action<object> OnPostDeserialization;

        /// <summary>
        /// Makes a deep copy of a given object using the serializer.
        /// </summary>
        /// <returns>
        /// The deep copy of a given object.
        /// </returns>
        /// <param name='toClone'>
        /// The object to make a deep copy of.
        /// </param>
        /// <param name='settings'>
        /// Settings used for serializer which does deep clone.
        /// </param>
        public static T DeepClone<T>(T toClone, Settings settings = null)
        {
            var serializer = new Serializer(settings);
            var stream = new MemoryStream();
            serializer.Serialize(toClone, stream);
            var position = stream.Position;
            stream.Seek(0, SeekOrigin.Begin);
            var result = serializer.Deserialize<T>(stream);
            if(position != stream.Position)
            {
                throw new InvalidOperationException(
                    string.Format("Internal error in serializer: {0} bytes were written, but only {1} were read.",
                        position, stream.Position));
            }
            return result;
        }

        private void WriteHeader(Stream stream)
        {
            stream.WriteByte(Magic1);
            stream.WriteByte(Magic2);
            stream.WriteByte(Magic3);
            stream.WriteByte(VersionNumber);
            stream.WriteByte(settings.ReferencePreservation == ReferencePreservation.DoNotPreserve ? (byte)0 : (byte)1);
        }

        private ObjectWriter ObtainWriter(Stream stream)
        {
            var writer = new ObjectWriter(stream, OnPreSerialization, OnPostSerialization, 
                             writeMethodCache, surrogatesForObjects, settings.SerializationMethod == Method.Generated, 
                             settings.TreatCollectionAsUserObject, settings.UseBuffering, settings.ReferencePreservation);
            return writer;
        }

        private DeserializationResult TryReadHeader(Stream stream, out bool preserveReferences)
        {
            preserveReferences = false;

            // Read header
            var magic1 = stream.ReadByte();
            var magic2 = stream.ReadByte();
            var magic3 = stream.ReadByte();
            if(magic1 != Magic1 || magic2 != Magic2 || magic3 != Magic3)
            {
                return DeserializationResult.WrongMagic;
            }
            var version = stream.ReadByte();
            if(version != VersionNumber)
            {
                return DeserializationResult.WrongVersion;
            }
            preserveReferences = stream.ReadByteOrThrow() != 0;
            return DeserializationResult.OK;
        }

        private void ThrowOnWrongResult(DeserializationResult result)
        {
            switch(result)
            {
            case DeserializationResult.OK:
                return;
            case DeserializationResult.WrongMagic:
                throw new InvalidOperationException("Cound not find proper magic.");
            case DeserializationResult.WrongVersion:
                throw new InvalidOperationException("Could not deserialize data serialized with another version of serializer.");
            case DeserializationResult.StreamCorrupted:
                throw lastException;
            default:
                throw new ArgumentOutOfRangeException();
            }
        }

        private ObjectReader ObtainReader(Stream stream, bool preserveReferences)
        {
            // user can only tell whether to use weak or strong reference preservation
            ReferencePreservation eventualPreservation;
            if(!preserveReferences)
            {
                eventualPreservation = ReferencePreservation.DoNotPreserve;
            }
            else
            {
                eventualPreservation = settings.ReferencePreservation == ReferencePreservation.UseWeakReference ? 
                    ReferencePreservation.UseWeakReference : ReferencePreservation.Preserve;
            }

            return new ObjectReader(stream, objectsForSurrogates, OnPostDeserialization, readMethodCache,
                settings.DeserializationMethod == Method.Generated, settings.TreatCollectionAsUserObject,
                settings.VersionTolerance, settings.UseBuffering, eventualPreservation);
        }

        private Exception lastException;
        private bool serializationDone;
        private bool deserializationDone;
        private readonly Settings settings;
        private readonly Dictionary<Type, Action<ObjectWriter, PrimitiveWriter, object>> writeMethodCache;
        private readonly Dictionary<Type, Func<ObjectReader, int, object>> readMethodCache;
        private readonly SwapList surrogatesForObjects;
        private readonly SwapList objectsForSurrogates;
        private const byte VersionNumber = 7;
        private const byte Magic1 = 0x32;
        private const byte Magic2 = 0x66;
        private const byte Magic3 = 0x34;

        /// <summary>
        /// Base class for surrogate setter.
        /// </summary>
        public class BaseObjectForSurrogateSetter
        {
            internal BaseObjectForSurrogateSetter(Serializer serializer)
            {
                Serializer = serializer;
            }

            internal void CheckLegality()
            {
                if(Serializer.deserializationDone)
                {
                    throw new InvalidOperationException("Cannot set objects for surrogates after any deserialization is done.");
                }
            }

            internal readonly Serializer Serializer;
        }

        /// <summary>
        /// Lets you set a callback providing object for type of the surrogate given to method that provided
        /// this object on a serializer that provided this object.
        /// </summary>
        public sealed class ObjectForSurrogateSetter<TSurrogate> : BaseObjectForSurrogateSetter
        {
            internal ObjectForSurrogateSetter(Serializer serializer) : base(serializer)
            {
            }

            /// <summary>
            /// Sets the callback proividing object for surrogate.
            /// </summary>
            /// <param name='callback'>
            /// Callback proividing object for surrogate. The callback can be null, in that case surrogate of the type
            /// <typeparamref name="TSurrogate" /> will be deserialized as is even if there is an object for the more
            /// general type.
            /// </param>
            /// <typeparam name='TObject'>
            /// The type of the object returned by callback.
            /// </typeparam>
            public void SetObject<TObject>(Func<TSurrogate, TObject> callback) where TObject : class
            {
                CheckLegality();
                Serializer.objectsForSurrogates.AddOrReplace(typeof(TSurrogate), callback);
            }
        }

        /// <summary>
        /// Lets you set a callback providing object for type of the surrogate given to method that provided
        /// this object on a serializer that provided this object.
        /// </summary>
        public sealed class ObjectForSurrogateSetter : BaseObjectForSurrogateSetter
        {
            internal ObjectForSurrogateSetter(Serializer serializer, Type type) : base(serializer)
            {
                this.type = type;
            }

            /// <summary>
            /// Sets the callback proividing object for surrogate.
            /// </summary>
            /// <param name="callback">
            /// Callback proividing object for surrogate. The callback can be null, in that case surrogate of the
            /// appropriate type will be deserialized as is even if there is an object for the more general type.
            /// </param>
            public void SetObject(Func<object, object> callback)
            {
                CheckLegality();
                Serializer.objectsForSurrogates.AddOrReplace(type, callback);
            }

            private readonly Type type;
        }

        /// <summary>
        /// Base class for object setter.
        /// </summary>
        public class BaseSurrogateForObjectSetter
        {
            internal BaseSurrogateForObjectSetter(Serializer serializer)
            {
                this.Serializer = serializer;
            }

            internal void CheckLegality()
            {
                if(Serializer.serializationDone)
                {
                    throw new InvalidOperationException("Cannot set surrogates for objects after any serialization is done.");
                }
            }

            internal readonly Serializer Serializer;
        }

        /// <summary>
        /// Lets you set a callback providing surrogate for type of the object given to method that provided
        /// this object on a serializer that provided this object.
        /// </summary>
        public sealed class SurrogateForObjectSetter<TObject> : BaseSurrogateForObjectSetter
        {
            internal SurrogateForObjectSetter(Serializer serializer) : base(serializer)
            {

            }

            /// <summary>
            /// Sets the callback providing surrogate for object.
            /// </summary>
            /// <param name='callback'>
            /// Callback providing surrogate for object. The callback can be null, in that case object of the type
            /// <typeparamref name="TObject" /> will be serialized as is even if there is a surrogate for the more
            /// general type.
            /// </param>
            /// <typeparam name='TSurrogate'>
            /// The type of the object returned by callback.
            /// </typeparam>
            public void SetSurrogate<TSurrogate>(Func<TObject, TSurrogate> callback) where TSurrogate : class
            {
                CheckLegality();
                Serializer.surrogatesForObjects.AddOrReplace(typeof(TObject), callback);
            }
        }

        /// <summary>
        /// Lets you set a callback providing surrogate for type of the object given to method that provided
        /// this object on a serializer that provided this object.
        /// </summary>
        public sealed class SurrogateForObjectSetter : BaseSurrogateForObjectSetter
        {
            internal SurrogateForObjectSetter(Serializer serializer, Type type) : base(serializer)
            {
                this.type = type;
            }

            /// <summary>
            /// Sets the callback providing surrogate for object.
            /// </summary>
            /// <param name='callback'>
            /// Callback providing surrogate for object. The callback can be null, in that case object of the
            /// appropriate type will be serialized as is even if there is a surrogate for the more general type.
            /// </param>
            public void SetSurrogate(Func<object, object> callback)
            {
                CheckLegality();
                Serializer.surrogatesForObjects.AddOrReplace(type, callback);
            }

            private readonly Type type;
        }

        /// <summary>
        /// Serializer that is attached to one stream and can do consecutive serializations that are aware of the data written
        /// by previous ones.
        /// </summary>
        public class OpenStreamSerializer : IDisposable
        {
            internal OpenStreamSerializer(ObjectWriter writer)
            {
                this.writer = writer;
            }

            /// <summary>
            /// Serialize the specified object.
            /// </summary>
            /// <param name="obj">Object.</param>
            public void Serialize(object obj)
            {
                writer.WriteObject(new Box(obj));
            }

            /// <summary>
            /// Flushes the buffer and necessary padding. Not necessary when buffering is not used.
            /// </summary>
            public void Dispose()
            {
                writer.Dispose();
            }

            private readonly ObjectWriter writer;
        }

        /// <summary>
        /// Deserializer that is attached to one stream and can do consecutive deserializations that are aware of the data written
        /// by previous ones.
        /// </summary>
        public class OpenStreamDeserializer : IDisposable
        {
            internal OpenStreamDeserializer(ObjectReader reader, Settings settings, Stream stream)
            {
                this.reader = reader;
                this.settings = settings;
                this.stream = stream;
            }

            /// <summary>
            /// Deserializes next object waiting in the stream.
            /// </summary>
            /// <typeparam name="T">The expected formal type of object to deserialize.</typeparam>
            public T Deserialize<T>()
            {
                var box = reader.ReadObject<Box>();
                return (T)box.Value;
            }

            /// <summary>
            /// Deserializes objects until end of the stream is reached. All objects have to be castable to T.
            /// This method is only available when buffering is disabled.
            /// </summary>
            /// <returns>Lazy collection of deserialized objects.</returns>
            /// <typeparam name="T">The expected type of object to deserialize.</typeparam>
            public IEnumerable<T> DeserializeMany<T>()
            {
                if(settings.UseBuffering)
                {
                    throw new NotSupportedException("DeserializeMany can be only used if buffering is disabled.");
                }
                var peekableStream = stream as PeekableStream;
                if(peekableStream == null)
                {
                    throw new NotSupportedException("Internal error: stream is not peekable.");
                }
                while(peekableStream.Peek() != -1)
                {
                    yield return Deserialize<T>();
                }
            }

            /// <summary>
            /// Reads leftover padding. Not necessary if buffering is not used.
            /// </summary>
            public void Dispose()
            {
                reader.Dispose();
            }

            private readonly ObjectReader reader;
            private readonly Settings settings;
            private readonly Stream stream;
        }
    }
}

