namespace SchedulingAgent.Prompts;

public static class ConflictResolutionPrompt
{
    public const string SystemPrompt = """
        Je bent een AI-beslissingsengine voor het oplossen van agendaconflicten.
        Analyseer het conflict en bepaal de beste strategie op basis van deze regels:

        BESLISSINGSMATRIX:
        - Informeel intern overleg → Probeer te verplaatsen (ProposeReschedule)
        - Externe meeting → Niet automatisch wijzigen (Escalate)
        - Meeting met externe deelnemers → Niet automatisch wijzigingen (Escalate)
        - Focus Time → Respecteren (SuggestAlternativeSlot)
        - All-day event → Niet aanpassen (SuggestAlternativeSlot)
        - Terugkerend overleg met lage prioriteit → Vraag deelnemer (AskParticipant)
        - Vergadering met hoge sensitivity → Niet wijzigen (Escalate)

        PRIORITEITSREGELS:
        - Urgent request kan lagere prioriteit events proberen te verplaatsen
        - High priority kan informele meetings proberen te verplaatsen
        - Normal priority moet alternatieven voorstellen
        - Low priority moet altijd alternatieven voorstellen

        Geef een gestructureerde JSON response met:
        - canAutoResolve: of het conflict automatisch opgelost kan worden
        - strategy: de gekozen strategie
        - reasoning: uitleg van de beslissing (in het Nederlands)
        - suggestedAlternativeSlot: optioneel alternatief tijdslot
        - blockedByUserId: ID van de blokkerende gebruiker
        - blockedByEventType: type van het blokkerende event
        """;

    public const string JsonSchema = """
        {
          "type": "object",
          "properties": {
            "canAutoResolve": { "type": "boolean" },
            "strategy": {
              "type": "string",
              "enum": ["ProposeReschedule", "AskParticipant", "SuggestAlternativeSlot", "ReduceAttendees", "Escalate"]
            },
            "reasoning": { "type": "string" },
            "suggestedAlternativeSlot": {
              "type": ["object", "null"],
              "properties": {
                "start": { "type": "string", "format": "date-time" },
                "end": { "type": "string", "format": "date-time" }
              },
              "required": ["start", "end"]
            },
            "blockedByUserId": { "type": ["string", "null"] },
            "blockedByEventType": { "type": ["string", "null"] }
          },
          "required": ["canAutoResolve", "strategy", "reasoning"]
        }
        """;
}
