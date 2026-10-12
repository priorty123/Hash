using System;
using System.Runtime.InteropServices;

namespace HashDash.Helpers
{
    internal static class NativeMethods
    {

                [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
[return: MarshalAs(UnmanagedType.Bool)]
public static extern bool GetDiskFreeSpaceEx(
    string lpDirectoryName,
    out long lpFreeBytesAvailable,
    out long lpTotalNumberOfBytes,
    out long lpTotalNumberOfFreeBytes);



        // CopyFile2 API (Windows 8+)
        [DllImport("kernel32.dll", PreserveSig = true, CharSet = CharSet.Unicode)]
        public static extern int CopyFile2(
            string pwszExistingFileName,
            string pwszNewFileName,
            ref COPYFILE2_EXTENDED_PARAMETERS pExtendedParameters);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        public delegate COPYFILE2_MESSAGE_ACTION CopyFile2ProgressRoutine(
            ref COPYFILE2_MESSAGE pMessage,
            IntPtr pvCallbackContext);

        [StructLayout(LayoutKind.Sequential)]
        public struct COPYFILE2_EXTENDED_PARAMETERS
        {
            public uint dwSize;
            public uint dwCopyFlags;
            public IntPtr pfCancel;
            public CopyFile2ProgressRoutine pProgressRoutine;
            public IntPtr pvCallbackContext;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct COPYFILE2_MESSAGE
        {
            public COPYFILE2_MESSAGE_TYPE Type;
            public uint dwPadding;
            public COPYFILE2_MESSAGE_UNION Info;
        }

        [StructLayout(LayoutKind.Explicit)]
        public struct COPYFILE2_MESSAGE_UNION
        {
            [FieldOffset(0)]
            public COPYFILE2_MESSAGE_CHUNKSTARTED ChunkStarted;

            [FieldOffset(0)]
            public COPYFILE2_MESSAGE_CHUNKFINISHED ChunkFinished;

            [FieldOffset(0)]
            public COPYFILE2_MESSAGE_STREAMSSTARTED StreamsStarted;

            [FieldOffset(0)]
            public COPYFILE2_MESSAGE_STREAMSFINISHED StreamsFinished;

            [FieldOffset(0)]
            public COPYFILE2_MESSAGE_POLLCONTINUE PollContinue;

            [FieldOffset(0)]
            public COPYFILE2_MESSAGE_ERROR Error;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct COPYFILE2_MESSAGE_CHUNKSTARTED
        {
            public uint dwStreamNumber;
            public uint dwReserved;
            public long liChunkNumber;
            public long liChunkSize;
            public long liStreamSize;
            public long liTotalFileSize;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct COPYFILE2_MESSAGE_CHUNKFINISHED
        {
            public uint dwStreamNumber;
            public uint dwFlags;
            public long liChunkNumber;
            public long liChunkSize;
            public long liStreamSize;
            public long liStreamBytesTransferred;
            public long liTotalFileSize;
            public long liTotalBytesTransferred;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct COPYFILE2_MESSAGE_STREAMSSTARTED
        {
            public uint dwStreamNumber;
            public uint dwReserved;
            public long liStreamSize;
            public long liTotalFileSize;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct COPYFILE2_MESSAGE_STREAMSFINISHED
        {
            public uint dwStreamNumber;
            public uint dwReserved;
            public long liStreamSize;
            public long liStreamBytesTransferred;
            public long liTotalFileSize;
            public long liTotalBytesTransferred;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct COPYFILE2_MESSAGE_POLLCONTINUE
        {
            public uint dwReserved;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct COPYFILE2_MESSAGE_ERROR
        {
            public COPYFILE2_COPY_PHASE CopyPhase;
            public uint dwStreamNumber;
            public int hrFailure;
            public uint dwReserved;
            public long liChunkNumber;
            public long liStreamSize;
            public long liStreamBytesTransferred;
            public long liTotalFileSize;
            public long liTotalBytesTransferred;
        }

        public enum COPYFILE2_MESSAGE_TYPE
        {
            COPYFILE2_CALLBACK_NONE = 0,
            COPYFILE2_CALLBACK_CHUNK_STARTED = 1,
            COPYFILE2_CALLBACK_CHUNK_FINISHED = 2,
            COPYFILE2_CALLBACK_STREAM_STARTED = 3,
            COPYFILE2_CALLBACK_STREAM_FINISHED = 4,
            COPYFILE2_CALLBACK_POLL_CONTINUE = 5,
            COPYFILE2_CALLBACK_ERROR = 6,
            COPYFILE2_CALLBACK_MAX = 7,
        }

        public enum COPYFILE2_MESSAGE_ACTION
        {
            COPYFILE2_PROGRESS_CONTINUE = 0,
            COPYFILE2_PROGRESS_CANCEL = 1,
            COPYFILE2_PROGRESS_STOP = 2,
            COPYFILE2_PROGRESS_QUIET = 3,
            COPYFILE2_PROGRESS_PAUSE = 4,
        }

        public enum COPYFILE2_COPY_PHASE
        {
            COPYFILE2_PHASE_NONE = 0,
            COPYFILE2_PHASE_PREPARE_SOURCE = 1,
            COPYFILE2_PHASE_PREPARE_DEST = 2,
            COPYFILE2_PHASE_READ_SOURCE = 3,
            COPYFILE2_PHASE_WRITE_DESTINATION = 4,
            COPYFILE2_PHASE_SERVER_COPY = 5,
            COPYFILE2_PHASE_NAMEGRAFT_COPY = 6,
            COPYFILE2_PHASE_MAX = 7,
        }

        // CopyFile2 flags
        public const uint COPY_FILE_FAIL_IF_EXISTS = 0x00000001;
        public const uint COPY_FILE_RESTARTABLE = 0x00000002;
        public const uint COPY_FILE_OPEN_SOURCE_FOR_WRITE = 0x00000004;
        public const uint COPY_FILE_ALLOW_DECRYPTED_DESTINATION = 0x00000008;
        public const uint COPY_FILE_COPY_SYMLINK = 0x00000800;
        public const uint COPY_FILE_NO_BUFFERING = 0x00001000;
        public const uint COPY_FILE_REQUEST_SECURITY_PRIVILEGES = 0x00002000;
        public const uint COPY_FILE_RESUME_FROM_PAUSE = 0x00004000;
        public const uint COPY_FILE_NO_OFFLOAD = 0x00040000;
        public const uint COPY_FILE_IGNORE_EDP_BLOCK = 0x00400000;
        public const uint COPY_FILE_IGNORE_SOURCE_ENCRYPTION = 0x00800000;

        // Constants
        public const int S_OK = 0;
        public const int E_ABORT = unchecked((int)0x80004004);
    }
}