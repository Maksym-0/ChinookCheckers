using ChinookCheckers.Application.DTOs;

namespace ChinookCheckers.Application.Interfaces;

public interface IMoveService
{
    Task<SuggestMoveResponse> SuggestMoveAsync(SuggestMoveRequest request, CancellationToken cancellationToken);

    ValidateMoveResponse ValidateMove(ValidateMoveRequest request);

    LegalMovesResponse GetLegalMoves(LegalMovesRequest request);
}
