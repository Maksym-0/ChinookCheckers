using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using ChinookCheckers.Application.Exceptions;
using ChinookCheckers.Core.Models;
using ChinookCheckers.Core.Options;
using ChinookCheckers.Infrastructure.Engine.Kingsrow.Native;
using Microsoft.Extensions.Logging;

namespace ChinookCheckers.Infrastructure.Engine.Kingsrow;

/// <summary>
/// KingsRow engine instance driven through the CheckerBoard API. The search is interrupted through the
/// API's <c>playnow</c> flag: the engine polls it and returns its best move so far as soon as it becomes 1.
/// </summary>
internal sealed unsafe class KingsrowEngineAdapter : IEngineAdapter
{
    // Pinned so the native engine can poll it through a stable pointer while another thread sets it.
    private readonly int[] _playNow = GC.AllocateArray<int>(1, pinned: true);
    private readonly EngineOptions _options;
    private readonly KingsrowLibraryProvisioner _provisioner;
    private readonly ILogger<KingsrowEngineAdapter> _logger;

    private KingsrowLibrary? _library;
    private Position? _position;
    private volatile bool _disposed;

    public KingsrowEngineAdapter(
        int workerId,
        EngineOptions options,
        KingsrowLibraryProvisioner provisioner,
        ILogger<KingsrowEngineAdapter> logger)
    {
        WorkerId = workerId;
        _options = options;
        _provisioner = provisioner;
        _logger = logger;
    }

    public int WorkerId { get; }

    public int TablebaseMaxPieces { get; private set; }

    public void Initialize()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_library is not null)
        {
            return;
        }

        var sourcePath = ResolvePath(_options.Path);
        _provisioner.EnsureWorkingDirectory(sourcePath);
        var library = KingsrowLibrary.Load(_provisioner.GetWorkerLibraryPath(sourcePath, WorkerId));

        SendCommand(library, $"set hashsize {_options.HashSizeMb}");
        SendCommand(library, $"set searchthreads {_options.SearchThreadsPerWorker}");
        TablebaseMaxPieces = ConfigureTablebase(library);

        foreach (var command in _options.AdditionalCommands)
        {
            SendCommand(library, command);
        }

        _library = library;
        _logger.LogInformation(
            "Engine worker {WorkerId} loaded {EngineName} from {LibraryPath}, tablebase up to {TablebaseMaxPieces} pieces",
            WorkerId,
            library.SendCommand("name"),
            library.Path,
            TablebaseMaxPieces);
    }

    public void WarmUp()
    {
        SetPosition(Position.Initial);
        var result = Search(new EngineSearchLimits(_options.WarmupTimeMs, null, false), CancellationToken.None);
        _logger.LogInformation(
            "Engine worker {WorkerId} warmed up in {ElapsedMs} ms (depth {Depth}, nodes {Nodes})",
            WorkerId,
            (long)result.Elapsed.TotalMilliseconds,
            result.Depth,
            result.Nodes);
    }

    public void SetPosition(Position position)
    {
        ArgumentNullException.ThrowIfNull(position);
        _position = position;
    }

    public EngineSearchResult Search(EngineSearchLimits limits, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(limits);
        ObjectDisposedException.ThrowIf(_disposed, this);

        var library = _library ?? throw new InvalidOperationException("Engine is not initialized.");
        var position = _position ?? throw new InvalidOperationException("Position is not set.");
        cancellationToken.ThrowIfCancellationRequested();

        var board = stackalloc int[CheckerBoardInterop.BoardCells];
        var info = stackalloc byte[CheckerBoardInterop.InfoBufferSize];
        var boardSpan = new Span<int>(board, CheckerBoardInterop.BoardCells);
        new Span<byte>(info, CheckerBoardInterop.InfoBufferSize).Clear();
        CheckerBoardInterop.WriteBoard(position, boardSpan);

        var move = default(CbMove);
        var flags = CheckerBoardInterop.FlagResetMoves
            | (_options.UseExactTime ? CheckerBoardInterop.FlagExactTime : 0);
        var playNow = (int*)Unsafe.AsPointer(ref MemoryMarshal.GetArrayDataReference(_playNow));

        Volatile.Write(ref _playNow[0], 0);
        var startedAt = Stopwatch.GetTimestamp();
        int outcome;

        // Disposing the registration waits for an in-flight callback, so no stale stop request can leak
        // into the next search on this worker.
        using (cancellationToken.UnsafeRegister(static state => ((KingsrowEngineAdapter)state!).RequestStop(), this))
        {
            outcome = library.GetMove(
                board,
                CheckerBoardInterop.ToColor(position.SideToMove),
                limits.SoftTimeMs / 1000.0,
                info,
                playNow,
                flags,
                0,
                &move);
        }

        var elapsed = Stopwatch.GetElapsedTime(startedAt);
        cancellationToken.ThrowIfCancellationRequested();

        info[CheckerBoardInterop.InfoBufferSize - 1] = 0;
        var infoText = Marshal.PtrToStringAnsi((nint)info);
        var parsed = EngineInfoParser.Parse(infoText, elapsed);

        int? wdl = outcome switch
        {
            CheckerBoardInterop.ResultWin => 1,
            CheckerBoardInterop.ResultLoss => -1,
            CheckerBoardInterop.ResultDraw => 0,
            // A database draw is not reported through the return code; it is recognised by the draw move list.
            _ when limits.ProbeTablebase && parsed.IsDrawMoveList => 0,
            _ => null
        };

        _logger.LogDebug(
            "Worker {WorkerId} search finished in {ElapsedMs} ms, outcome {Outcome}, info: {EngineInfo}",
            WorkerId,
            (long)elapsed.TotalMilliseconds,
            outcome,
            infoText);

        return new EngineSearchResult(
            ResultingPosition: CheckerBoardInterop.ReadBoard(boardSpan, position.SideToMove.Opponent()),
            BestMoveNotation: parsed.BestMove,
            PrincipalVariation: parsed.PrincipalVariation,
            Score: parsed.Score,
            Wdl: wdl,
            Depth: parsed.Depth,
            Nodes: parsed.Nodes,
            TablebaseHit: limits.ProbeTablebase && wdl is not null,
            Elapsed: elapsed);
    }

    public void RequestStop() => Volatile.Write(ref _playNow[0], 1);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        RequestStop();
        _library = null;
    }

    /// <returns>Largest piece count the engine can resolve from the database, 0 when probing is disabled.</returns>
    private int ConfigureTablebase(KingsrowLibrary library)
    {
        if (string.IsNullOrWhiteSpace(_options.Databases))
        {
            SendCommand(library, "set enable_wld 0");
            return 0;
        }

        var searchRoot = ResolvePath(_options.Databases);
        var database = EndgameDatabaseLocator.Locate(searchRoot);

        if (database is null)
        {
            _logger.LogWarning(
                "No KingsRow WLD database (db*.idx1 + db*.cpr1, 2..N pieces) found under {DatabasePath}; " +
                "worker {WorkerId} runs without tablebase probing",
                searchRoot,
                WorkerId);
            SendCommand(library, "set enable_wld 0");
            return 0;
        }

        SendCommand(library, $"set dbmbytes {_options.DbCacheMb}");
        SendCommand(library, database.Directory.Any(char.IsWhiteSpace)
            ? $"set dbpath \"{database.Directory}\""
            : $"set dbpath {database.Directory}");
        SendCommand(library, "set enable_wld 1");

        _logger.LogInformation(
            "Worker {WorkerId} uses WLD database in {DatabaseDirectory}: up to {MaxPieces} pieces, {FileCount} files, {SizeGb:F2} GB",
            WorkerId,
            database.Directory,
            database.MaxPieces,
            database.FileCount,
            database.SizeBytes / (1024.0 * 1024 * 1024));

        return Math.Min(database.MaxPieces, _options.TablebaseMaxPieces);
    }

    private void SendCommand(KingsrowLibrary library, string command)
    {
        var reply = library.SendCommand(command);
        if (reply.Contains("Unrecognized", StringComparison.OrdinalIgnoreCase))
        {
            throw new EngineException($"Engine rejected command '{command}': {reply}");
        }

        _logger.LogDebug("Worker {WorkerId} engine command '{Command}' -> '{Reply}'", WorkerId, command, reply);
    }

    private static string ResolvePath(string path) =>
        Path.IsPathRooted(path) ? path : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, path));
}
