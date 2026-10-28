const GAME_ID = "checkers-8x8";

export class ApiError extends Error {
    constructor(message, status = 0) {
        super(message);
        this.name = "ApiError";
        this.status = status;
    }
}

/** Легальні ходи позиції + позиція після кожного ходу (POST /v1/move/legal). */
export const getLegalMoves = (position, signal) =>
    post("/v1/move/legal", { position }, signal);

/** Рекомендований хід рушія (POST /v1/move/suggest з ТЗ). */
export const suggestMove = (position, level, signal) =>
    post("/v1/move/suggest", {
        gameId: GAME_ID,
        state: { notation: "PDN", position },
        level
    }, signal);

async function post(url, body, signal) {
    let response;
    try {
        response = await fetch(url, {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify(body),
            signal
        });
    } catch (error) {
        if (error.name === "AbortError") throw error;
        throw new ApiError("Немає зв'язку з сервером.");
    }

    if (!response.ok) throw new ApiError(await describeFailure(response), response.status);
    return response.json();
}

async function describeFailure(response) {
    let detail = "";
    try {
        const problem = await response.json();
        detail = problem.detail ?? Object.values(problem.errors ?? {}).flat().join(" ") ?? "";
    } catch {
        // тіло відповіді не JSON - достатньо статусу
    }

    switch (response.status) {
        case 504: return "Рушій не встиг відповісти за відведений час.";
        case 503: return "Рушій тимчасово недоступний.";
        case 422: return detail || "Позиція некоректна.";
        case 400: return detail || "Некоректний запит.";
        default: return `Помилка сервера (${response.status}).`;
    }
}
