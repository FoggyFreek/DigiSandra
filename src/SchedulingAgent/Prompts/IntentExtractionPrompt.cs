namespace SchedulingAgent.Prompts;

public static class IntentExtractionPrompt
{
    public const string SystemPrompt = """
        Je bent een intelligente planningsassistent die vergaderverzoeken analyseert.
        Extraheer de volgende informatie uit het bericht van de gebruiker:

        1. subject: Het onderwerp van de vergadering
        2. durationMinutes: De duur in minuten (standaard 60 als niet gespecificeerd)
        3. timeWindow: Het gewenste tijdsvenster met startDate en endDate (ISO 8601 formaat)
           - "volgende week" = maandag t/m vrijdag van de volgende werkweek
           - "morgen" = de volgende werkdag
           - "deze week" = resterende werkdagen van de huidige week
        4. participants: Lijst van deelnemers met naam en type (User of Group)
        5. priority: Low, Normal, High of Urgent (standaard Normal)
        6. isOnline: Of het een online vergadering moet zijn (standaard true)
        7. notes: Eventuele aanvullende opmerkingen

        Gebruik de huidige datum als referentie. Geef altijd gestructureerde JSON output.
        Als informatie ontbreekt, gebruik redelijke standaardwaarden.
        """;

    public const string JsonSchema = """
        {
          "type": "object",
          "properties": {
            "subject": { "type": "string" },
            "durationMinutes": { "type": "integer" },
            "timeWindow": {
              "type": "object",
              "properties": {
                "startDate": { "type": "string", "format": "date-time" },
                "endDate": { "type": "string", "format": "date-time" },
                "preferredTimeOfDay": { "type": ["string", "null"], "enum": ["Morning", "Afternoon", "Evening", null] }
              },
              "required": ["startDate", "endDate"]
            },
            "participants": {
              "type": "array",
              "items": {
                "type": "object",
                "properties": {
                  "name": { "type": "string" },
                  "type": { "type": "string", "enum": ["User", "Group", "DistributionList"] },
                  "isRequired": { "type": "boolean" }
                },
                "required": ["name", "type", "isRequired"]
              }
            },
            "priority": { "type": "string", "enum": ["Low", "Normal", "High", "Urgent"] },
            "isOnline": { "type": "boolean" },
            "notes": { "type": ["string", "null"] }
          },
          "required": ["subject", "durationMinutes", "timeWindow", "participants", "priority", "isOnline"]
        }
        """;
}
