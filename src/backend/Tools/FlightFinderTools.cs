using System.ComponentModel;
using System.Text.Json;
using ContosoTravelAgent.Host.Models;
using Microsoft.Azure.Cosmos;
using OpenAI.Embeddings;

namespace ContosoTravelAgent.Host.Tools;

public class FlightFinderTools
{
    private readonly Database _database;
    private readonly ContosoTravelAppConfig _config;
    private readonly EmbeddingClient _embeddingClient;

    /// <summary>
    /// Initializes a new instance of the FlightFinderTools class.
    /// </summary>
    /// <param name="database">Cosmos DB database instance.</param>
    /// <param name="config">Application configuration.</param>
    /// <param name="embeddingClient">Embedding client for vector search.</param>
    public FlightFinderTools(Database database, ContosoTravelAppConfig config, EmbeddingClient embeddingClient)
    {
        _database = database;
        _config = config;
        _embeddingClient = embeddingClient;
    }

    /// <summary>
    /// Searches for available flights between two cities.
    /// </summary>
    [Description("Search for available flights between two cities. Returns structured flight options with prices, times, and airline details. Supports semantic matching based on user preferences.")]
    public async Task<string> SearchFlights(
        [Description("Departure city or airport (e.g., 'Melbourne', 'MEL')")] string origin,
        [Description("Destination city or airport (e.g., 'Tokyo', 'NRT', 'Paris', 'CDG')")] string destination,
        [Description("Departure date in YYYY-MM-DD format (optional)")] string? departureDate = null,
        [Description("Return date in YYYY-MM-DD format (optional)")] string? returnDate = null,
        [Description("Maximum budget in AUD (optional)")] decimal? maxBudget = null,
        [Description("User preferences for flight characteristics (e.g., 'comfortable flight with entertainment', 'budget-friendly', 'business travel') (optional)")] string? userPreferences = null)
    {
        // Query flights from Cosmos DB
        var container = _database.GetContainer(_config.CosmosDbFlightsContainer!);

        // Generate embedding for user preferences if provided
        float[]? preferenceVector = null;
        if (!string.IsNullOrEmpty(userPreferences))
        {
            var embeddingResponse = await _embeddingClient.GenerateEmbeddingAsync(userPreferences);
            preferenceVector = embeddingResponse.Value.ToFloats().ToArray();
        }

        // Build query with native vector search if preferences provided
        QueryDefinition queryDefinition;
        if (preferenceVector != null)
        {
            var queryText = @"SELECT c, VectorDistance(c.flightProfileVector, @preferenceVector) AS SimilarityScore
                FROM c 
                WHERE c.type = 'flight' 
                AND (CONTAINS(UPPER(c.route.origin.city), @origin) OR CONTAINS(UPPER(c.route.origin.code), @origin))
                AND (CONTAINS(UPPER(c.route.destination.city), @destination) OR CONTAINS(UPPER(c.route.destination.code), @destination))
                ORDER BY VectorDistance(c.flightProfileVector, @preferenceVector)";

            queryDefinition = new QueryDefinition(queryText)
                .WithParameter("@origin", origin.ToUpper())
                .WithParameter("@destination", destination.ToUpper())
                .WithParameter("@preferenceVector", preferenceVector);
        }
        else
        {
            var queryText = @"SELECT * FROM c 
                WHERE c.type = 'flight' 
                AND (CONTAINS(UPPER(c.route.origin.city), @origin) OR CONTAINS(UPPER(c.route.origin.code), @origin))
                AND (CONTAINS(UPPER(c.route.destination.city), @destination) OR CONTAINS(UPPER(c.route.destination.code), @destination))";

            queryDefinition = new QueryDefinition(queryText)
                .WithParameter("@origin", origin.ToUpper())
                .WithParameter("@destination", destination.ToUpper());
        }

        var flights = new List<FlightOption>();

        using var iterator = container.GetItemQueryIterator<JsonDocument>(queryDefinition);

        while (iterator.HasMoreResults)
        {
            var response = await iterator.ReadNextAsync();
            foreach (var doc in response)
            {
                var root = doc.RootElement;

                // When using SELECT c with computed columns, the document is nested under "c"
                var flightDoc = root.TryGetProperty("c", out var cProp) ? cProp : root;
                var flight = new FlightOption
                {
                    FlightNumber = flightDoc.GetProperty("flightNumber").GetString()!,
                    Airline = flightDoc.GetProperty("airline").GetProperty("name").GetString()!,
                    Price = flightDoc.GetProperty("pricing").GetProperty("amount").GetDecimal(),
                    DepartureTime = DateTime.Parse($"2026-01-31T{flightDoc.GetProperty("schedule").GetProperty("departure").GetString()}"),
                    ArrivalTime = DateTime.Parse($"2026-01-31T{flightDoc.GetProperty("schedule").GetProperty("arrival").GetString()}"),
                    FlightProfile = flightDoc.TryGetProperty("flightProfile", out var profileProp) ? profileProp.GetString() : null
                };

                // Get similarity score from query result if available
                if (root.TryGetProperty("SimilarityScore", out var scoreProp))
                {
                    // Cosmos DB returns distance (lower is better), convert to similarity (higher is better)
                    flight.SimilarityScore = 1.0 - scoreProp.GetDouble();
                }

                flights.Add(flight);
            }
        }

        // Filter by budget if provided - demonstrates business logic
        var filteredFlights = maxBudget.HasValue
            ? flights.Where(f => f.Price <= maxBudget.Value).ToList()
            : flights;

        // Results are already ordered by vector search if preferences provided, otherwise order by price
        var orderedFlights = !string.IsNullOrEmpty(userPreferences)
            ? filteredFlights // Already sorted by VectorDistance in query
            : filteredFlights.OrderBy(f => f.Price).ToList();

        // Return structured JSON response
        var result = new
        {
            success = true,
            searchCriteria = new
            {
                origin,
                destination,
                maxBudget = maxBudget?.ToString("C") ?? "No limit",
                userPreferences = userPreferences ?? "None",
                semanticSearchEnabled = !string.IsNullOrEmpty(userPreferences)
            },
            totalResults = filteredFlights.Count,
            flights = orderedFlights,
            message = filteredFlights.Count > 0
                ? $"Found {filteredFlights.Count} flight options for {origin} to {destination}" +
                  (!string.IsNullOrEmpty(userPreferences) ? " (ranked by preference match)" : "")
                : $"No flights found within budget constraint of {maxBudget:C}"
        };

        return JsonSerializer.Serialize(result, new JsonSerializerOptions
        {
            WriteIndented = true
        });
    }
}
