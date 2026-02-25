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
           - "bij voorkeur op woensdag" → preferredDaysOfWeek: ["Wednesday"]
           - preferredDaysOfWeek is optioneel; laat het weg als geen dag-voorkeur aanwezig
        4. participants: Lijst van deelnemers met naam en type (User of Group)
        5. priority: Low, Normal, High of Urgent (standaard Normal)
        6. isOnline: Of het een online vergadering moet zijn (standaard true)
        7. room: Of het in een fysieke ruimte moet plaatsvinden (indien isOnline false)
        8. notes: Eventuele aanvullende opmerkingen
        9. recurrence: Herhaalpatroon als de gebruiker meerdere vergaderingen wil plannen.
           - "3 wekelijkse vergaderingen" → count: 3, frequency: "Weekly", intervalWeeks: 1
           - "tweewekelijks" → frequency: "BiWeekly", intervalWeeks: 2
           - "maandelijks" → frequency: "Monthly"
           - Als er geen herhaling is, laat recurrence dan weg (null).
           - timeWindow moet het volledige bereik beslaan (startDate t/m de laatste vergadering).

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
                "preferredTimeOfDay": { "type": ["string", "null"], "enum": ["Morning", "Afternoon", "Evening", null] },
                "preferredDaysOfWeek": {
                  "type": ["array", "null"],
                  "items": { "type": "string", "enum": ["Sunday","Monday","Tuesday","Wednesday","Thursday","Friday","Saturday"] }
                }
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
            "room": { "type": ["string", "null"] },
            "notes": { "type": ["string", "null"] },
            "recurrence": {
              "type": ["object", "null"],
              "properties": {
                "count": { "type": "integer", "minimum": 2 },
                "frequency": { "type": "string", "enum": ["Weekly", "BiWeekly", "Monthly"] },
                "intervalWeeks": { "type": "integer", "minimum": 1 }
              },
              "required": ["count", "frequency", "intervalWeeks"]
            }
          },
          "required": ["subject", "durationMinutes", "timeWindow", "participants", "priority", "isOnline"]
        }
        """;
}
