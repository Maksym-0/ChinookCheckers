/*
 * Позиція: squares[0..31] -> клітинки 1..32 у стандартній нумерації PDN.
 * Клітинки 1-4 у верхньому ряду (бік Black), 29-32 у нижньому (бік White) - так само, як BoardGeometry на сервері.
 * Значення клітинки: null | { color: "white" | "black", king: boolean }.
 * Правил гри тут немає: легальні ходи завжди приходять із сервера (POST /v1/move/legal).
 */

export const SQUARE_COUNT = 32;
export const BOARD_SIZE = 8;
export const START_POSITION = "B:W21-32:B1-12";

const MAX_PIECES_PER_SIDE = 12;

export const opponent = color => (color === "white" ? "black" : "white");

export const isDarkSquare = (row, col) => (row + col) % 2 === 1;
export const toSquareNumber = (row, col) => row * 4 + Math.floor(col / 2) + 1;

const isKingRow = (square, color) => (color === "black" ? square >= 29 : square <= 4);

/** Дзеркало PdnParser з Core: приймає також [FEN "..."], діапазони (5-7) та префікс K для дамок. */
export function parsePdn(text) {
    let fen = (text ?? "").trim();
    if (!fen) throw new Error("Позиція порожня.");

    if (/^\[FEN/i.test(fen) && fen.endsWith("]")) fen = fen.slice(4, -1).trim();
    fen = fen.replace(/^"+|"+$/g, "").replace(/\.+$/, "").trim();

    const sections = fen.split(":").map(s => s.trim());
    if (sections.length !== 3) {
        throw new Error("Формат: <хто ходить>:W<клітинки>:B<клітинки>, наприклад B:W21-32:B1-12.");
    }

    const sideToMove = parseColor(sections[0], "хто ходить");
    const squares = new Array(SQUARE_COUNT).fill(null);
    const seen = new Set();

    for (const section of sections.slice(1)) {
        if (!section) throw new Error("Список фігур має починатися з 'W' або 'B'.");

        const color = parseColor(section[0], "список фігур");
        if (seen.has(color)) throw new Error(`Список фігур для ${color} вказано двічі.`);
        seen.add(color);

        parsePieceList(section.slice(1), color, squares);
    }

    validate(squares);
    return { squares, sideToMove };
}

export function toPdn(position) {
    const list = color => position.squares
        .map((piece, index) => (piece?.color === color ? (piece.king ? "K" : "") + (index + 1) : null))
        .filter(Boolean)
        .join(",");

    return `${position.sideToMove === "black" ? "B" : "W"}:W${list("white")}:B${list("black")}`;
}

export const countPieces = (position, color) => position.squares.filter(p => p?.color === color).length;

function parseColor(token, context) {
    switch (token.toUpperCase()) {
        case "B": return "black";
        case "W": return "white";
        default: throw new Error(`Некоректне значення (${context}): '${token}', очікується W або B.`);
    }
}

function parsePieceList(list, color, squares) {
    for (const raw of list.split(",").map(s => s.trim()).filter(Boolean)) {
        const king = /^k/i.test(raw);
        const bounds = (king ? raw.slice(1) : raw).split("-").map(s => s.trim());

        const first = Number(bounds[0]);
        const last = Number(bounds[bounds.length - 1]);
        const valid = bounds.length <= 2
            && bounds.every(b => /^\d+$/.test(b))
            && first >= 1 && last <= SQUARE_COUNT && first <= last;

        if (!valid) throw new Error(`Некоректна клітинка '${raw}': допустимо 1..32.`);

        for (let square = first; square <= last; square++) {
            if (squares[square - 1]) throw new Error(`Клітинка ${square} зайнята більше ніж однією фігурою.`);
            squares[square - 1] = { color, king };
        }
    }
}

function validate(squares) {
    const count = color => squares.filter(p => p?.color === color).length;
    const black = count("black");
    const white = count("white");

    if (black > MAX_PIECES_PER_SIDE || white > MAX_PIECES_PER_SIDE) {
        throw new Error(`Максимум ${MAX_PIECES_PER_SIDE} фігур на сторону (чорні: ${black}, білі: ${white}).`);
    }
    if (black + white === 0) throw new Error("У позиції немає фігур.");

    squares.forEach((piece, index) => {
        if (piece && !piece.king && isKingRow(index + 1, piece.color)) {
            throw new Error(`Проста фігура не може стояти на дамочному полі (клітинка ${index + 1}).`);
        }
    });
}
