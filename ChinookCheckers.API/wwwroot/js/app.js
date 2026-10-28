import {
    BOARD_SIZE,
    START_POSITION,
    countPieces,
    isDarkSquare,
    opponent,
    parsePdn,
    toPdn,
    toSquareNumber
} from "./pdn.js";
import { getLegalMoves, suggestMove } from "./api.js";

const HINT_LEVEL = "strong";
const DRAW_HALFMOVE_LIMIT = 80; // 40 ходів кожного боку без взяття і ходу простою фігурою
const REPETITION_LIMIT = 3;
const COLOR_LABEL = { white: "білі", black: "чорні" };
const COLOR_GENITIVE = { white: "білих", black: "чорних" };

const els = {
    board: document.getElementById("board"),
    statusBar: document.getElementById("status-bar"),
    statusText: document.getElementById("status-text"),
    spinner: document.getElementById("spinner"),
    btnRetry: document.getElementById("btn-retry"),
    turnChip: document.getElementById("turn-chip"),
    counts: document.getElementById("counts"),
    engineInfo: document.getElementById("engine-info"),
    moves: document.getElementById("moves"),
    movesScroll: document.getElementById("moves-scroll"),
    side: document.getElementById("sel-side"),
    level: document.getElementById("sel-level"),
    btnNew: document.getElementById("btn-new"),
    btnHint: document.getElementById("btn-hint"),
    btnUndo: document.getElementById("btn-undo"),
    btnFlip: document.getElementById("btn-flip"),
    chkNumbers: document.getElementById("chk-numbers"),
    pdn: document.getElementById("pdn"),
    btnLoad: document.getElementById("btn-load"),
    pdnMessage: document.getElementById("pdn-message")
};

/*
 * phase:
 *   loading - чекаємо легальні ходи від сервера
 *   human   - хід людини, дошка інтерактивна
 *   engine  - рушій обчислює відповідь
 *   over    - гру завершено (game.result)
 *   error   - збій запиту до API (game.error), можна повторити
 */
const game = {
    humanColor: "white",
    timeline: [],          // [{ position, key, by, mover, move, halfmoveClock }]
    legalMoves: [],        // ходи поточної позиції з сервера
    phase: "loading",
    selection: null,       // { path: number[] } - обрана фігура і пройдені клітинки серії взять
    hint: null,            // { path, notation }
    result: null,
    error: null,
    notice: null,          // нефатальне повідомлення (наприклад, збій підказки)
    retry: null,
    engineInfo: null,
    flipped: false,
    hintBusy: false,
    token: 0,              // змінюється при скасуванні, щоб відкинути застарілі відповіді
    controllers: new Set()
};

const current = () => game.timeline[game.timeline.length - 1];
const isAbort = error => error?.name === "AbortError";
const samePath = (a, b) => a.length === b.length && a.every((square, i) => square === b[i]);

/* ------------------------------------------------------------------ керування грою */

function createEntry(position, by, move, halfmoveClock) {
    return {
        position,
        key: toPdn(position),
        by,
        mover: by ? opponent(position.sideToMove) : null,
        move,
        halfmoveClock
    };
}

function startGame(position, humanColor) {
    cancelPending();
    game.humanColor = humanColor;
    game.timeline = [createEntry(position, null, null, 0)];
    game.flipped = humanColor === "black";
    resetTransientState();
    beginTurn();
}

function resetTransientState() {
    game.legalMoves = [];
    game.selection = null;
    game.hint = null;
    game.result = null;
    game.error = null;
    game.notice = null;
    game.retry = null;
    game.engineInfo = null;
}

function startNewGame() {
    const choice = els.side.value;
    const color = choice === "random" ? (Math.random() < 0.5 ? "white" : "black") : choice;
    startGame(parsePdn(START_POSITION), color);
}

async function track(call) {
    const controller = new AbortController();
    game.controllers.add(controller);
    try {
        return await call(controller.signal);
    } finally {
        game.controllers.delete(controller);
    }
}

function cancelPending() {
    game.token++;
    game.controllers.forEach(controller => controller.abort());
    game.controllers.clear();
    game.hintBusy = false;
}

function fail(error, token, retry) {
    if (isAbort(error) || token !== game.token) return;
    game.phase = "error";
    game.error = error.message;
    game.retry = retry;
    render();
}

async function beginTurn() {
    const token = game.token;
    const { position } = current();

    game.phase = "loading";
    game.error = null;
    game.selection = null;
    render();

    let legal;
    try {
        legal = await track(signal => getLegalMoves(toPdn(position), signal));
    } catch (error) {
        fail(error, token, beginTurn);
        return;
    }
    if (token !== game.token) return;

    game.legalMoves = legal.moves;

    if (legal.moves.length === 0) {
        finish({ type: "win", winner: legal.winner });
        return;
    }

    const drawReason = detectDraw();
    if (drawReason) {
        finish({ type: "draw", reason: drawReason });
        return;
    }

    if (position.sideToMove === game.humanColor) {
        game.phase = "human";
        render();
    } else {
        await engineTurn(token);
    }
}

function detectDraw() {
    const { key, halfmoveClock } = current();
    if (game.timeline.filter(entry => entry.key === key).length >= REPETITION_LIMIT) {
        return "триразове повторення позиції";
    }
    if (halfmoveClock >= DRAW_HALFMOVE_LIMIT) {
        return "40 ходів без взяття та ходу простою фігурою";
    }
    return null;
}

function finish(result) {
    game.phase = "over";
    game.result = result;
    render();
}

async function engineTurn(token) {
    game.phase = "engine";
    render();

    const retry = () => engineTurn(game.token);
    let suggestion;
    try {
        suggestion = await track(signal => suggestMove(toPdn(current().position), els.level.value, signal));
    } catch (error) {
        fail(error, token, retry);
        return;
    }
    if (token !== game.token) return;

    const move = findMove(suggestion.bestMove);
    if (!move) {
        fail(new Error("Рушій повернув хід, якого немає серед легальних."), token, retry);
        return;
    }

    game.engineInfo = describeSuggestion("Рушій", suggestion, current().position);
    commitMove(move, "engine");
    await beginTurn();
}

function findMove(notation) {
    return game.legalMoves.find(move => move.notation === notation) ?? null;
}

function commitMove(move, by) {
    const previous = current();
    const piece = previous.position.squares[move.path[0] - 1];
    const resetsClock = move.captured.length > 0 || !piece.king;

    game.timeline.push(createEntry(
        parsePdn(move.resultingPosition),
        by,
        move,
        resetsClock ? 0 : previous.halfmoveClock + 1));

    game.selection = null;
    game.hint = null;
    game.notice = null;
    game.legalMoves = [];
}

function playHumanMove(move) {
    commitMove(move, "human");
    beginTurn();
}

async function requestHint() {
    if (game.phase !== "human" || game.hintBusy) return;

    const token = game.token;
    game.hintBusy = true;
    game.notice = null;
    game.selection = null;
    render();

    try {
        const position = current().position;
        const suggestion = await track(signal => suggestMove(toPdn(position), HINT_LEVEL, signal));
        if (token !== game.token) return;

        const move = findMove(suggestion.bestMove);
        if (!move) throw new Error("Рушій повернув хід, якого немає серед легальних.");

        game.hint = { path: move.path, notation: move.notation };
        game.engineInfo = describeSuggestion("Підказка", suggestion, position);
    } catch (error) {
        if (isAbort(error) || token !== game.token) return;
        game.notice = `Підказка недоступна: ${error.message}`;
    } finally {
        if (token === game.token) {
            game.hintBusy = false;
            render();
        }
    }
}

function undo() {
    if (!game.timeline.some(entry => entry.by === "human")) return;

    cancelPending();
    while (game.timeline.length > 1) {
        const removed = game.timeline.pop();
        if (removed.by === "human") break;
    }

    resetTransientState();
    beginTurn();
}

/* ------------------------------------------------------------------ взаємодія з дошкою */

function candidateMoves(path) {
    return game.legalMoves.filter(move => move.path.length > path.length
        && path.every((square, i) => move.path[i] === square));
}

function originSquares() {
    return new Set(game.legalMoves.map(move => move.path[0]));
}

function onSquareClick(square) {
    if (game.phase !== "human") return;

    game.hint = null;
    game.notice = null;
    const selection = game.selection;

    if (selection) {
        const nextPath = [...selection.path, square];
        const candidates = candidateMoves(selection.path);

        if (candidates.some(move => move.path[selection.path.length] === square)) {
            const complete = game.legalMoves.find(move => samePath(move.path, nextPath));
            if (complete) {
                playHumanMove(complete);
                return;
            }
            game.selection = { path: nextPath };
            render();
            return;
        }

        // Посеред серії взять фігуру не можна змінити - лише скасувати вибір кліком по ній.
        if (selection.path.length > 1) {
            if (square === selection.path[0]) game.selection = null;
            render();
            return;
        }
    }

    game.selection = originSquares().has(square) && square !== selection?.path[0]
        ? { path: [square] }
        : null;
    render();
}

els.board.addEventListener("click", event => {
    const cell = event.target.closest(".square.dark");
    if (cell) onSquareClick(Number(cell.dataset.square));
});

document.addEventListener("keydown", event => {
    if (event.key === "Escape" && game.selection) {
        game.selection = null;
        render();
    }
});

/* ------------------------------------------------------------------ рендер */

function render() {
    renderBoard();
    renderStatus();
    renderMoves();
    renderControls();
}

function computeMarks() {
    const marks = new Map();
    const mark = (square, name) => {
        if (!marks.has(square)) marks.set(square, new Set());
        marks.get(square).add(name);
    };

    const last = current().move;
    if (last) {
        mark(last.path[0], "last-move");
        mark(last.path[last.path.length - 1], "last-move");
    }

    if (game.hint) {
        game.hint.path.forEach((square, i, path) => {
            mark(square, i === 0 ? "hint-from" : i === path.length - 1 ? "hint-to" : "hint-step");
        });
    }

    if (game.phase !== "human") return marks;

    if (game.selection) {
        const { path } = game.selection;
        mark(path[0], "selected");
        path.slice(1).forEach(square => mark(square, "path"));
        candidateMoves(path).forEach(move => mark(move.path[path.length], "target"));
    } else {
        originSquares().forEach(square => mark(square, "movable"));
    }

    return marks;
}

function renderBoard() {
    const position = current().position;
    const marks = computeMarks();
    const fragment = document.createDocumentFragment();

    for (let displayRow = 0; displayRow < BOARD_SIZE; displayRow++) {
        for (let displayCol = 0; displayCol < BOARD_SIZE; displayCol++) {
            // Розворот дошки - поворот на 180 градусів, тому нумерація клітинок не змінюється.
            const row = game.flipped ? BOARD_SIZE - 1 - displayRow : displayRow;
            const col = game.flipped ? BOARD_SIZE - 1 - displayCol : displayCol;

            const cell = document.createElement("div");
            cell.setAttribute("role", "gridcell");

            if (!isDarkSquare(row, col)) {
                cell.className = "square";
                fragment.appendChild(cell);
                continue;
            }

            const square = toSquareNumber(row, col);
            const piece = position.squares[square - 1];
            const classes = ["square", "dark", ...(marks.get(square) ?? [])];
            if (classes.includes("movable") || classes.includes("target")) classes.push("clickable");

            cell.className = classes.join(" ");
            cell.dataset.square = String(square);
            cell.setAttribute("aria-label", describeSquare(square, piece));

            const number = document.createElement("span");
            number.className = "square-number";
            number.textContent = String(square);
            cell.appendChild(number);

            if (piece) {
                const el = document.createElement("div");
                el.className = `piece ${piece.color}${piece.king ? " king" : ""}`;
                cell.appendChild(el);
            }

            fragment.appendChild(cell);
        }
    }

    els.board.replaceChildren(fragment);
}

function describeSquare(square, piece) {
    if (!piece) return `Клітинка ${square}, порожня`;
    const color = piece.color === "white" ? "біла" : "чорна";
    return `Клітинка ${square}, ${color} ${piece.king ? "дамка" : "шашка"}`;
}

function statusView() {
    const mine = COLOR_LABEL[game.humanColor];
    const mustCapture = game.legalMoves.some(move => move.captured.length > 0);

    switch (game.phase) {
        case "loading": return { text: "Завантаження…", busy: true };
        case "engine": return { text: "Рушій думає…", busy: true };
        case "error": return { text: game.error, error: true, retry: true };
        case "over": return { text: resultText(), final: true };
        default:
            if (game.selection?.path.length > 1) return { text: "Продовжіть взяття: оберіть наступну клітинку." };
            return { text: `Ваш хід (${mine})${mustCapture ? " - обов'язкове взяття" : ""}` };
    }
}

function resultText() {
    const { result } = game;
    if (result.type === "draw") return `Нічия: ${result.reason}.`;
    return result.winner === game.humanColor
        ? "Перемога! У рушія немає ходів."
        : "Поразка: у вас немає ходів.";
}

function renderStatus() {
    const view = statusView();
    const text = game.notice && game.phase === "human" ? game.notice : view.text;

    els.statusText.textContent = text;
    els.spinner.hidden = !view.busy;
    els.btnRetry.hidden = !view.retry;
    els.statusBar.classList.toggle("error", Boolean(view.error) || Boolean(game.notice && game.phase === "human"));
    els.statusBar.classList.toggle("final", Boolean(view.final));

    const { position } = current();
    els.turnChip.className = `chip ${position.sideToMove}`;
    els.turnChip.title = `Ходять ${COLOR_LABEL[position.sideToMove]}`;
    els.counts.textContent = `Білі: ${countPieces(position, "white")}  ·  Чорні: ${countPieces(position, "black")}`;
    els.engineInfo.textContent = game.engineInfo ?? "";
    els.engineInfo.hidden = !game.engineInfo;

    if (document.activeElement !== els.pdn) els.pdn.value = toPdn(position);
}

function describeSuggestion(title, suggestion, position) {
    const parts = [`${title}: ${suggestion.bestMove}`];

    if (suggestion.info.tablebaseHit) {
        const verdict = { 1: "виграш", 0: "нічия", "-1": "програш" }[suggestion.scoreOrWDL];
        parts.push(`ендшпільна база${verdict ? `: ${verdict} для ${COLOR_GENITIVE[position.sideToMove]}` : ""}`);
    } else {
        parts.push(`глибина ${suggestion.depth}`);
    }
    parts.push(`${suggestion.info.timeMs} мс`);

    return parts.join(" · ");
}

function renderMoves() {
    const rows = [];
    for (const { mover, move } of game.timeline) {
        if (!move) continue;
        if (mover === "black") {
            rows.push({ black: move.notation, white: "" });
        } else {
            const last = rows.at(-1);
            if (last && last.black && !last.white) last.white = move.notation;
            else rows.push({ black: "…", white: move.notation });
        }
    }

    const fragment = document.createDocumentFragment();
    rows.forEach((row, index) => {
        const tr = document.createElement("tr");
        for (const text of [String(index + 1), row.black, row.white]) {
            const td = document.createElement("td");
            td.textContent = text;
            tr.appendChild(td);
        }
        fragment.appendChild(tr);
    });

    els.moves.replaceChildren(fragment);
    els.movesScroll.scrollTop = els.movesScroll.scrollHeight;
}

function renderControls() {
    els.btnHint.disabled = game.phase !== "human" || game.hintBusy;
    els.btnHint.textContent = game.hintBusy ? "Підказка…" : "Підказка";
    els.btnUndo.disabled = !game.timeline.some(entry => entry.by === "human");
}

/* ------------------------------------------------------------------ керування */

els.btnNew.addEventListener("click", startNewGame);
els.btnHint.addEventListener("click", requestHint);
els.btnUndo.addEventListener("click", undo);
els.btnRetry.addEventListener("click", () => game.retry?.());

els.btnFlip.addEventListener("click", () => {
    game.flipped = !game.flipped;
    render();
});

els.chkNumbers.addEventListener("change", () => {
    els.board.classList.toggle("hide-numbers", !els.chkNumbers.checked);
});

function loadFromEditor() {
    try {
        const position = parsePdn(els.pdn.value);
        const choice = els.side.value;
        const color = choice === "random" ? (Math.random() < 0.5 ? "white" : "black") : choice;

        els.pdnMessage.textContent = "";
        startGame(position, color);
    } catch (error) {
        els.pdnMessage.textContent = error.message;
    }
}

els.btnLoad.addEventListener("click", loadFromEditor);
els.pdn.addEventListener("keydown", event => {
    if (event.key === "Enter" && (event.ctrlKey || event.metaKey)) loadFromEditor();
});

startNewGame();
