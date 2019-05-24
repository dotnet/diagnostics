using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Microsoft.Diagnostics.Tools.RuntimeClient.DiagnosticsIpc
{
    public class IpcMessage
    {
        public IpcMessage()
        { }

        public IpcMessage(IpcHeader header, byte[] payload)
        {
            Payload = payload;
            Header = header;
        }

        internal IpcMessage(DiagnosticServerCommandSet commandSet, byte commandId, byte[] payload = null)
        {
            Header = new IpcHeader(commandSet, commandId);
            Payload = payload;
        }

        public byte[] Payload { get; private set; } = null;
        public IpcHeader Header { get; private set; } = default;

        public byte[] Serialize()
        { 
            byte[] serializedData = null;
            // Verify things will fit in the size capacity
            Header.Size = checked((UInt16)(IpcHeader.HeaderSizeInBytes + Payload.Length)); ;
            byte[] headerBytes = Header.Serialize();

            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(headerBytes);
                writer.Write(Payload);
                writer.Flush();
                serializedData = stream.ToArray();
            }

            return serializedData;
        }

        public static IpcMessage Parse(Stream stream)
        {
            IpcMessage message = new IpcMessage();
            using (var reader = new BinaryReader(stream, Encoding.UTF8, true))
            {
                message.Header = IpcHeader.TryParse(reader);
                message.Payload = reader.ReadBytes(message.Header.Size - IpcHeader.HeaderSizeInBytes);
                return message;
            }
        }
    }
}
