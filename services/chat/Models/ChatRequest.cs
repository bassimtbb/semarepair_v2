namespace ChatService.Models;

// POST /api/chat/stream body. See docs/SemaRepair_Architecture.md section 6.3
// and Rule 5 (confirmed car persists for the whole session).
public class ChatRequest
{
    public string SessionId { get; set; } = "";
    public string Message { get; set; } = "";

    // The mechanic's specific car pick (idMacchina), when the frontend has
    // one - e.g. a click on a FindCar/SearchBySymptom result card. This is
    // the only unambiguous way to confirm a car: engine codes aren't
    // unique even within one brand (verified live - one engine code,
    // 8140.43S, matches 5 different IVECO Daily III trims alone), so
    // confirming by engineCode+brand alone picks an arbitrary row when
    // more than one matches. See ConfirmCarAsync.
    public string? ConfirmedCarId { get; set; }

    public string? ConfirmedCodiceMotore { get; set; }

    // Engine codes aren't unique across brands in real data (verified
    // during Search Service work: one engine code spanned 4 brands) - so
    // confirming a car needs both, matching SearchRequest/VehicleQuery's
    // own codiceMotore+marca scoping. Used as a fallback only when
    // ConfirmedCarId isn't supplied - see ConfirmCarAsync.
    public string? ConfirmedMarca { get; set; }

    public string Language { get; set; } = "it";
}
