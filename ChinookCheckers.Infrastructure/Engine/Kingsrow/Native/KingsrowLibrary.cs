using System.Runtime.InteropServices;
using ChinookCheckers.Application.Exceptions;

namespace ChinookCheckers.Infrastructure.Engine.Kingsrow.Native;

/// <summary>
/// One loaded module of Kingsrow64.dll. KingsRow keeps its search state in DLL globals, so each worker
/// needs its own module (a separate file copy). [DllImport] binds one module per process, therefore the
/// exports are resolved through NativeLibrary into unmanaged function pointers of this instance.
/// </summary>
internal sealed unsafe class KingsrowLibrary
{
    private readonly delegate* unmanaged[Stdcall]<int*, int, double, byte*, int*, int, int, CbMove*, int> _getMove;
    private readonly delegate* unmanaged[Stdcall]<byte*, byte*, int> _engineCommand;

    private KingsrowLibrary(string path, nint handle)
    {
        Path = path;
        _getMove = (delegate* unmanaged[Stdcall]<int*, int, double, byte*, int*, int, int, CbMove*, int>)
            GetExport(handle, "getmove");
        _engineCommand = (delegate* unmanaged[Stdcall]<byte*, byte*, int>)GetExport(handle, "enginecommand");
    }

    public string Path { get; }

    // The module is intentionally never freed: KingsRow may keep helper threads alive inside it and
    // FreeLibrary under a running thread crashes the process. The OS reclaims it on process exit.
    public static KingsrowLibrary Load(string path)
    {
        if (!Environment.Is64BitProcess)
        {
            throw new EngineException("Kingsrow64.dll requires a 64-bit process (disable 32-bit mode in the IIS app pool).");
        }

        try
        {
            return new KingsrowLibrary(path, NativeLibrary.Load(path));
        }
        catch (Exception ex) when (ex is DllNotFoundException or BadImageFormatException or EntryPointNotFoundException)
        {
            throw new EngineException($"Failed to load KingsRow engine from '{path}'.", ex);
        }
    }

    /// <summary>CheckerBoard <c>getmove</c>: searches, writes the move into <paramref name="board"/> and returns WIN/LOSS/DRAW/UNKNOWN.</summary>
    public int GetMove(
        int* board,
        int color,
        double maxTimeSeconds,
        byte* info,
        int* playNow,
        int flags,
        int moreInfo,
        CbMove* move) =>
        _getMove(board, color, maxTimeSeconds, info, playNow, flags, moreInfo, move);

    public string SendCommand(string command)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(command);

        var ansi = Marshal.StringToHGlobalAnsi(command);
        try
        {
            var length = MemoryMarshal.CreateReadOnlySpanFromNullTerminated((byte*)ansi).Length;
            if (length >= CheckerBoardInterop.CommandBufferSize)
            {
                throw new ArgumentException(
                    $"Engine command exceeds {CheckerBoardInterop.CommandBufferSize - 1} bytes.", nameof(command));
            }

            var commandBuffer = stackalloc byte[CheckerBoardInterop.CommandBufferSize];
            var replyBuffer = stackalloc byte[CheckerBoardInterop.ReplyBufferSize];
            new Span<byte>(commandBuffer, CheckerBoardInterop.CommandBufferSize).Clear();
            new Span<byte>(replyBuffer, CheckerBoardInterop.ReplyBufferSize).Clear();
            Buffer.MemoryCopy((void*)ansi, commandBuffer, CheckerBoardInterop.CommandBufferSize, length);

            _engineCommand(commandBuffer, replyBuffer);
            replyBuffer[CheckerBoardInterop.ReplyBufferSize - 1] = 0;
            return Marshal.PtrToStringAnsi((nint)replyBuffer) ?? string.Empty;
        }
        finally
        {
            Marshal.FreeHGlobal(ansi);
        }
    }

    private static nint GetExport(nint handle, string name)
    {
        try
        {
            return NativeLibrary.GetExport(handle, name);
        }
        catch (EntryPointNotFoundException ex)
        {
            throw new EngineException($"Export '{name}' not found: the DLL does not implement the CheckerBoard API.", ex);
        }
    }
}
