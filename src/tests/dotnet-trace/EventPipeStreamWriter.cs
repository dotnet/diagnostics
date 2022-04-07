// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.Tools.Common;

// Copy/Paste from https://github.com/microsoft/perfview/blob/7b7661eeec6b5af1e11b0c2293b8f9be7fdc0456/src/TraceEvent/TraceEvent.Tests/Parsing/EventPipeParsing.cs#L599-L1010

namespace Microsoft.Diagnostics.Tools.Trace
{
    internal class MockStreamingOnlyStream : Stream
    {
        Stream _innerStream;
        public MockStreamingOnlyStream(Stream innerStream)
        {
            _innerStream = innerStream;
        }
        public long TestOnlyPosition { get { return _innerStream.Position; } }


        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotImplementedException();
        public override long Position { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
        public override void Flush()
        {
            throw new NotImplementedException();
        }
        public override int Read(byte[] buffer, int offset, int count)
        {
            return _innerStream.Read(buffer, offset, count);
        }
        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotImplementedException();
        }
        public override void SetLength(long value)
        {
            throw new NotImplementedException();
        }
        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotImplementedException();
        }
    }


    internal class EventMetadata
    {
        public EventMetadata(int metadataId, string providerName, string eventName, int eventId)
        {
            MetadataId = metadataId;
            ProviderName = providerName;
            EventName = eventName;
            EventId = eventId;
        }


        public int MetadataId { get; set; }
        public string ProviderName { get; set; }
        public string EventName { get; set; }
        public int EventId { get; set; }
    }


    internal class EventPayloadWriter
    {
        BinaryWriter _writer = new BinaryWriter(new MemoryStream());


        public void Write(string arg)
        {
            _writer.Write(Encoding.Unicode.GetBytes(arg));
            _writer.Write((ushort)0);
        }


        public void WriteArray<T>(T[] elements, Action<T> writeElement)
        {
            WriteArrayLength(elements.Length);
            for (int i = 0; i < elements.Length; i++)
            {
                writeElement(elements[i]);
            }
        }


        public void WriteArrayLength(int length)
        {
            _writer.Write((ushort)length);
        }


        public byte[] ToArray()
        {
            return (_writer.BaseStream as MemoryStream).ToArray();
        }
    }


    internal class EventPipeWriter
    {
        BinaryWriter _writer;


        public EventPipeWriter()
        {
            _writer = new BinaryWriter(new MemoryStream());
        }


        public byte[] ToArray()
        {
            return (_writer.BaseStream as MemoryStream).ToArray();
        }


        public void WriteHeaders()
        {
            WriteNetTraceHeader(_writer);
            WriteFastSerializationHeader(_writer);
            WriteTraceObject(_writer);
        }


        public void WriteMetadataBlock(params EventMetadata[] metadataBlobs)
        {
            WriteMetadataBlock(_writer, metadataBlobs);
        }


        public void WriteEventBlock(Action<BinaryWriter> writeEventBlobs)
        {
            WriteEventBlock(_writer, writeEventBlobs);
        }


        public void WriteEndObject()
        {
            WriteEndObject(_writer);
        }


        public static void WriteNetTraceHeader(BinaryWriter writer)
        {
            writer.Write(Encoding.UTF8.GetBytes("Nettrace"));
        }


        public static void WriteFastSerializationHeader(BinaryWriter writer)
        {
            WriteString(writer, "!FastSerialization.1");
        }


        public static void WriteString(BinaryWriter writer, string val)
        {
            writer.Write(val.Length);
            writer.Write(Encoding.UTF8.GetBytes(val));
        }


        public static void WriteObject(BinaryWriter writer, string name, int version, int minVersion, Action writePayload)
        {
            writer.Write((byte)5); // begin private object
            writer.Write((byte)5); // begin private object - type
            writer.Write((byte)1); // type of type
            writer.Write(version);
            writer.Write(minVersion);
            WriteString(writer, name);
            writer.Write((byte)6); // end object
            writePayload();
            writer.Write((byte)6); // end object
        }


        public static void WriteTraceObject(BinaryWriter writer)
        {
            WriteObject(writer, "Trace", 4, 4, () =>
            {
                DateTime now = DateTime.Now;
                writer.Write((short)now.Year);
                writer.Write((short)now.Month);
                writer.Write((short)now.DayOfWeek);
                writer.Write((short)now.Day);
                writer.Write((short)now.Hour);
                writer.Write((short)now.Minute);
                writer.Write((short)now.Second);
                writer.Write((short)now.Millisecond);
                writer.Write((long)1_000_000); // syncTimeQPC
                writer.Write((long)1000); // qpcFreq
                writer.Write(8); // pointer size
                writer.Write(1); // pid
                writer.Write(4); // num procs
                writer.Write(1000); // sampling rate
            });
        }


        private static void Align(BinaryWriter writer, long previousBytesWritten)
        {
            int offset = (int)((writer.BaseStream.Position + previousBytesWritten) % 4);
            if (offset != 0)
            {
                for (int i = offset; i < 4; i++)
                {
                    writer.Write((byte)0);
                }
            }
        }


        public static void WriteBlock(BinaryWriter writer, string name, Action<BinaryWriter> writeBlockData,
            long previousBytesWritten = 0)
        {
            Debug.WriteLine($"Starting block {name} position: {writer.BaseStream.Position + previousBytesWritten}");
            MemoryStream block = new MemoryStream();
            BinaryWriter blockWriter = new BinaryWriter(block);
            writeBlockData(blockWriter);
            WriteObject(writer, name, 2, 0, () =>
            {
                writer.Write((int)block.Length);
                Align(writer, previousBytesWritten);
                writer.Write(block.GetBuffer(), 0, (int)block.Length);
            });
        }


        public static void WriteMetadataBlock(BinaryWriter writer, Action<BinaryWriter> writeMetadataEventBlobs, long previousBytesWritten = 0)
        {
            WriteBlock(writer, "MetadataBlock", w =>
            {
                // header
                w.Write((short)20); // header size
                w.Write((short)0); // flags
                w.Write((long)0);  // min timestamp
                w.Write((long)0);  // max timestamp
                writeMetadataEventBlobs(w);
            },
            previousBytesWritten);
        }


        public static void WriteMetadataBlock(BinaryWriter writer, EventMetadata[] metadataBlobs, long previousBytesWritten = 0)
        {
            WriteMetadataBlock(writer,
                w =>
                {
                    foreach (EventMetadata blob in metadataBlobs)
                    {
                        WriteMetadataEventBlob(w, blob);
                    }
                },
                previousBytesWritten);
        }


        public static void WriteMetadataBlock(BinaryWriter writer, params EventMetadata[] metadataBlobs)
        {
            WriteMetadataBlock(writer, metadataBlobs, 0);
        }


        public static void WriteMetadataEventBlob(BinaryWriter writer, EventMetadata eventMetadataBlob)
        {
            MemoryStream payload = new MemoryStream();
            BinaryWriter payloadWriter = new BinaryWriter(payload);
            payloadWriter.Write(eventMetadataBlob.MetadataId);           // metadata id
            payloadWriter.Write(Encoding.Unicode.GetBytes(eventMetadataBlob.ProviderName));  // provider name
            payloadWriter.Write((short)0);                               // null terminator
            payloadWriter.Write(eventMetadataBlob.EventId);              // event id
            payloadWriter.Write(Encoding.Unicode.GetBytes(eventMetadataBlob.EventName)); // event name
            payloadWriter.Write((short)0);                               // null terminator
            payloadWriter.Write((long)0);                                // keywords
            payloadWriter.Write(1);                                      // version
            payloadWriter.Write(5);                                      // level
            payloadWriter.Write(0);                                      // fieldcount


            MemoryStream eventBlob = new MemoryStream();
            BinaryWriter eventWriter = new BinaryWriter(eventBlob);
            eventWriter.Write(0);                                        // metadata id
            eventWriter.Write(0);                                        // sequence number
            eventWriter.Write((long)999);                                // thread id
            eventWriter.Write((long)999);                                // capture thread id
            eventWriter.Write(1);                                        // proc number
            eventWriter.Write(0);                                        // stack id
            eventWriter.Write((long)123456789);                          // timestamp
            eventWriter.Write(Guid.Empty.ToByteArray());                 // activity id
            eventWriter.Write(Guid.Empty.ToByteArray());                 // related activity id
            eventWriter.Write((int)payload.Length);                      // payload size
            eventWriter.Write(payload.GetBuffer(), 0, (int)payload.Length);


            writer.Write((int)eventBlob.Length);
            writer.Write(eventBlob.GetBuffer(), 0, (int)eventBlob.Length);
        }


        public static void WriteEventBlock(BinaryWriter writer, Action<BinaryWriter> writeEventBlobs, long previousBytesWritten = 0)
        {
            WriteBlock(writer, "EventBlock", w =>
            {
                // header
                w.Write((short)20); // header size
                w.Write((short)0);  // flags
                w.Write((long)0);   // min timestamp
                w.Write((long)0);   // max timestamp
                writeEventBlobs(w);
            },
            previousBytesWritten);
        }


        public static void WriteEventBlob(BinaryWriter writer, int metadataId, int sequenceNumber, int payloadSize, Action<BinaryWriter> writeEventPayload)
        {
            MemoryStream eventBlob = new MemoryStream();
            BinaryWriter eventWriter = new BinaryWriter(eventBlob);
            eventWriter.Write(metadataId);                               // metadata id
            eventWriter.Write(sequenceNumber);                           // sequence number
            eventWriter.Write((long)999);                                // thread id
            eventWriter.Write((long)999);                                // capture thread id
            eventWriter.Write(1);                                        // proc number
            eventWriter.Write(0);                                        // stack id
            eventWriter.Write((long)123456789);                          // timestamp
            eventWriter.Write(Guid.Empty.ToByteArray());                 // activity id
            eventWriter.Write(Guid.Empty.ToByteArray());                 // related activity id
            eventWriter.Write(payloadSize);                              // payload size


            writer.Write((int)eventBlob.Length + payloadSize);
            writer.Write(eventBlob.GetBuffer(), 0, (int)eventBlob.Length);
            long beforePayloadPosition = writer.BaseStream.Position;
            writeEventPayload(writer);
            long afterPayloadPosition = writer.BaseStream.Position;
            Debug.Assert(afterPayloadPosition - beforePayloadPosition == payloadSize);
        }


        public static void WriteEventBlob(BinaryWriter writer, int metadataId, int sequenceNumber, byte[] payloadBytes)
        {
            WriteEventBlob(writer, metadataId, sequenceNumber, payloadBytes.Length, w => w.Write(payloadBytes));
        }


        public static void WriteEndObject(BinaryWriter writer)
        {
            writer.Write(1); // null tag
        }
    }


    internal class MockHugeStream : Stream
    {
        // the events are big to make the stream grow fast
        const int payloadSize = 60000;

        MemoryStream _currentChunk = new MemoryStream();
        long _minSize;
        long _bytesWritten;
        int _sequenceNumber = 1;

        public MockHugeStream(long minSize)
        {
            _minSize = minSize;
            _currentChunk = GetFirstChunk();
            _bytesWritten = _currentChunk.Length;
        }

        MemoryStream GetFirstChunk()
        {
            MemoryStream ms = new MemoryStream();
            BinaryWriter writer = new BinaryWriter(ms);
            EventPipeWriter.WriteNetTraceHeader(writer);
            EventPipeWriter.WriteFastSerializationHeader(writer);
            EventPipeWriter.WriteTraceObject(writer);
            EventPipeWriter.WriteMetadataBlock(writer,
                new EventMetadata(1, "Provider", "Event", 1));
            ms.Position = 0;
            return ms;
        }

        MemoryStream GetNextChunk()
        {
            MemoryStream ms = new MemoryStream();
            BinaryWriter writer = new BinaryWriter(ms);
            if (_bytesWritten > _minSize)
            {
                EventPipeWriter.WriteEndObject(writer);
            }
            else
            {
                // 20 blocks, each with 20 events in them
                for (int i = 0; i < 20; i++)
                {
                    EventPipeWriter.WriteEventBlock(writer,
                        w =>
                        {
                            for (int j = 0; j < 20; j++)
                            {
                                EventPipeWriter.WriteEventBlob(w, 1, _sequenceNumber++, payloadSize, WriteEventPayload);
                            }
                        },
                        _bytesWritten);
                }
            }
            ms.Position = 0;
            return ms;
        }

        static void WriteEventPayload(BinaryWriter writer)
        {
            for (int i = 0; i < payloadSize / 8; i++)
            {
                writer.Write((long)i);
            }
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotImplementedException();
        public override long Position { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
        public override void Flush()
        {
            throw new NotImplementedException();
        }
        public override int Read(byte[] buffer, int offset, int count)
        {
            int ret = _currentChunk.Read(buffer, offset, count);
            if (ret == 0)
            {
                _currentChunk = GetNextChunk();
                _bytesWritten += _currentChunk.Length;
                ret = _currentChunk.Read(buffer, offset, count);
            }
            return ret;
        }
        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotImplementedException();
        }
        public override void SetLength(long value)
        {
            throw new NotImplementedException();
        }
        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotImplementedException();
        }
    }

    internal class MockMultiEventStream : Stream
    {
        // relatively small so we can write a lot of events
        const int payloadSize = 8;

        MemoryStream _currentChunk = new MemoryStream();
        (string, string, int)[] _events;
        int _eventsIndex = 0;
        long _bytesWritten;
        int _sequenceNumber = 1;

        /// <summary>
        /// Create a mock stream of EventPipe data with events defined in <param>events</param>
        /// events is a list of (provider, eventname, count) tuples. The event id is implicitly the index+1 in the list.
        /// </summary>
        public MockMultiEventStream(IEnumerable<(string, string, int)> events)
        {
            Debug.Assert(events.Any());
            _events = events.ToArray();
            _currentChunk = GetFirstChunk();
            _bytesWritten = _currentChunk.Length;
        }

        MemoryStream GetFirstChunk()
        {
            MemoryStream ms = new MemoryStream();
            BinaryWriter writer = new BinaryWriter(ms);
            EventPipeWriter.WriteNetTraceHeader(writer);
            EventPipeWriter.WriteFastSerializationHeader(writer);
            EventPipeWriter.WriteTraceObject(writer);
            EventPipeWriter.WriteMetadataBlock(writer, 
                _events.Select(((string providerName, string eventName, int _) tup, int index) => new EventMetadata(index + 1, tup.providerName, tup.eventName, index + 1)).ToArray());
            ms.Position = 0;
            return ms;
        }

        MemoryStream GetNextChunk()
        {
            MemoryStream ms = new MemoryStream();
            BinaryWriter writer = new BinaryWriter(ms);
            if (_eventsIndex == _events.Length)
            {
                EventPipeWriter.WriteEndObject(writer);
            }
            else
            {
                // send as many <=20 event sized blocks to send the correct number of events
                (string providerName, string eventName, int count) = _events[_eventsIndex];
                int nBlocks = (int)Math.Ceiling((double)count / 20);
                int eventsWritten = 0;
                for (int i = 0; i < nBlocks; i++)
                {
                    EventPipeWriter.WriteEventBlock(writer,
                        w =>
                        {
                            for (int j = 0; j < 20 && eventsWritten < count; j++)
                            {
                                eventsWritten++;
                                EventPipeWriter.WriteEventBlob(w, _eventsIndex+1, _sequenceNumber++, payloadSize, WriteEventPayload);
                            }
                        },
                        _bytesWritten);
                }
                _eventsIndex++;
            }
            ms.Position = 0;
            return ms;
        }

        static void WriteEventPayload(BinaryWriter writer)
        {
            for (int i = 0; i < payloadSize / 8; i++)
            {
                writer.Write((long)i);
            }
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotImplementedException();
        public override long Position { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
        public override void Flush()
        {
            throw new NotImplementedException();
        }
        public override int Read(byte[] buffer, int offset, int count)
        {
            int ret = _currentChunk.Read(buffer, offset, count);
            if (ret == 0)
            {
                _currentChunk = GetNextChunk();
                _bytesWritten += _currentChunk.Length;
                ret = _currentChunk.Read(buffer, offset, count);
            }
            return ret;
        }
        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotImplementedException();
        }
        public override void SetLength(long value)
        {
            throw new NotImplementedException();
        }
        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotImplementedException();
        }
    }
}