using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;

namespace Microsoft.Diagnostics.Tools.RuntimeClient.DiagnosticsIpc
{
    public class IpcHeader
    {
        IpcHeader() { }

        public IpcHeader(DiagnosticServerCommandSet commandSet, byte commandId)
        {
            CommandSet = (byte)commandSet;
            CommandId = commandId;
        }

        // the number of bytes for the DiagnosticsIpc::IpcHeader type in native code
        public static readonly UInt16 HeaderSizeInBytes = 20;
        private static readonly UInt16 MagicSizeInBytes = 14;

        public byte[] Magic = ASCIIEncoding.ASCII.GetBytes("DOTNET_IPC_V1" + '\0'); // byte[14] in native code
        public UInt16 Size = HeaderSizeInBytes;
        public byte CommandSet;
        public byte CommandId;
        public UInt16 Reserved = 0x0000;


        // Helper expression to quickly get V1 magic string for comparison
        // should be 14 bytes long
        public static byte[] DOTNET_IPC_V1 => ASCIIEncoding.ASCII.GetBytes("DOTNET_IPC_V1" + '\0');

        public byte[] Serialize()
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(Magic);
                Debug.Assert(Magic.Length == MagicSizeInBytes);
                writer.Write(Size);
                writer.Write(CommandSet);
                writer.Write(CommandId);
                writer.Write((UInt16)0x0000);
                writer.Flush();
                return stream.ToArray();
            }
        }

        public static IpcHeader TryParse(BinaryReader reader)
        {
            IpcHeader header = new IpcHeader
            {
                Magic = reader.ReadBytes(14),
                Size = reader.ReadUInt16(),
                CommandSet = reader.ReadByte(),
                CommandId = reader.ReadByte(),
                Reserved = reader.ReadUInt16()
            };

            // TODO: Validate it is correct!

            return header;
        }

        override public string ToString()
        {
            return $"{{ Magic={Magic}; Size={Size}; CommandSet={CommandSet}; CommandId={CommandId}; Reserved={Reserved} }}";
        }
    }
}
