using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using MySqlConnector;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.ComponentModel.DataAnnotations;

namespace CSharpBackend.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [EnableRateLimiting("sliding")]
    public class TripsController : ControllerBase
    {
        private const string UsersTable = "userssl";
        private const string ListingTable = "postedadd";

        private static readonly HashSet<string> SupportedListingTypes = new(StringComparer.OrdinalIgnoreCase)
        {
            "floor",
            "couch",
            "bed"
        };

        private readonly IConfiguration _configuration;
        
        public TripsController(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        [HttpPost]
        [EnableRateLimiting("token")] 
        public IActionResult SaveTrip([FromBody] CreateTripRequest dto)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            try
            {
                if (dto is null)
                    return BadRequest(new { error = "Request body is required" });

                dto.Name = dto.Name?.Trim();

                if (dto.UserId <= 0)
                    return BadRequest(new { error = "Invalid user_id" });

                if (string.IsNullOrWhiteSpace(dto.Name))
                    return BadRequest(new { error = "name is required" });

                var type = dto.Type?.Trim();
                if (string.IsNullOrWhiteSpace(type) || !SupportedListingTypes.Contains(type))
                    return BadRequest(new { error = "type must be one of: floor, couch, bed" });

                if (dto.Price <= 0)
                    return BadRequest(new { error = "price must be greater than 0" });

                if (dto.Location is null)
                    return BadRequest(new { error = "location is required" });

                if (dto.Location.Lat < -90 || dto.Location.Lat > 90 || dto.Location.Lng < -180 || dto.Location.Lng > 180)
                    return BadRequest(new { error = "location coordinates are out of range" });

                var createdAt = dto.CreatedAt == default ? DateTime.UtcNow : dto.CreatedAt;
                var mapData = string.IsNullOrWhiteSpace(dto.MapData) ? "{}" : dto.MapData;

                using var conn = new MySqlConnection(_configuration.GetConnectionString("DefaultConnection"));
                conn.Open();
                
                using var cmd = new MySqlCommand(
                    $@"INSERT INTO {ListingTable} (user_id, name, type, price, location_lat, location_lng, map_data, created_at)
                      VALUES (@user_id, @name, @type, @price, @location_lat, @location_lng, @map_data, @created_at)",
                    conn);

                cmd.Parameters.AddWithValue("@user_id", dto.UserId);
                cmd.Parameters.AddWithValue("@name", dto.Name);
                cmd.Parameters.AddWithValue("@type", type);
                cmd.Parameters.AddWithValue("@price", dto.Price);
                cmd.Parameters.AddWithValue("@location_lat", dto.Location.Lat);
                cmd.Parameters.AddWithValue("@location_lng", dto.Location.Lng);
                cmd.Parameters.AddWithValue("@map_data", mapData);
                cmd.Parameters.AddWithValue("@created_at", createdAt);

                cmd.ExecuteNonQuery();
                long insertId = cmd.LastInsertedId;

                return StatusCode(201, new { id = insertId, message = "Listing saved successfully" });
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                return StatusCode(500, new { error = "Failed to save listing" });
            }
        }

        [HttpGet("{id}")]
        public IActionResult GetTrip(int id)
        {
            if (id <= 0)
                return BadRequest(new { error = "Invalid listing ID" });

            try
            {
                using var conn = new MySqlConnection(_configuration.GetConnectionString("DefaultConnection"));
                conn.Open();

                using var cmd = new MySqlCommand($@"
                    SELECT p.id,
                           p.user_id,
                           p.name,
                           p.type,
                           p.price,
                           p.location_lat,
                           p.location_lng,
                           p.map_data,
                           p.created_at,
                           u.username,
                           (SELECT COUNT(*) FROM trip_interests WHERE trip_id = p.id) as interest_count
                    FROM {ListingTable} p
                    LEFT JOIN {UsersTable} u ON p.user_id = u.id
                    WHERE p.id = @id", conn);
                cmd.Parameters.AddWithValue("@id", id);

                using var reader = cmd.ExecuteReader();
                if (!reader.Read())
                    return NotFound(new { error = "Listing not found" });

                var result = new
                {
                    id = reader.GetInt64("id"),
                    user_id = reader.IsDBNull(reader.GetOrdinal("user_id")) ? (int?)null : reader.GetInt32("user_id"),
                    name = reader.GetString("name"),
                    type = reader.IsDBNull(reader.GetOrdinal("type")) ? null : reader.GetString("type"),
                    price = reader.IsDBNull(reader.GetOrdinal("price")) ? (decimal?)null : reader.GetDecimal("price"),
                    location_lat = reader.IsDBNull(reader.GetOrdinal("location_lat")) ? (double?)null : reader.GetDouble("location_lat"),
                    location_lng = reader.IsDBNull(reader.GetOrdinal("location_lng")) ? (double?)null : reader.GetDouble("location_lng"),
                    map_data = reader.IsDBNull(reader.GetOrdinal("map_data")) ? "{}" : reader.GetString("map_data"),
                    created_at = reader.GetDateTime("created_at"),
                    owner_name = reader.IsDBNull(reader.GetOrdinal("username")) ? "Unknown" : reader.GetString("username"),
                    interest_count = reader.IsDBNull(reader.GetOrdinal("interest_count")) ? 0 : reader.GetInt32("interest_count")
                };

                return Ok(result);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                return StatusCode(500, new { error = "Failed to load listing" });
            }
        }

        [HttpGet]
        public IActionResult GetAllTrips()
        {
            try
            {
                var listings = new List<object>();

                using var conn = new MySqlConnection(_configuration.GetConnectionString("DefaultConnection"));
                conn.Open();

                using var cmd = new MySqlCommand($@"
                    SELECT p.id,
                           p.user_id,
                           p.name,
                           p.type,
                           p.price,
                           p.location_lat,
                           p.location_lng,
                           p.map_data,
                           p.created_at,
                           u.username,
                           (SELECT COUNT(*) FROM trip_interests WHERE trip_id = p.id) as interest_count
                    FROM {ListingTable} p
                    LEFT JOIN {UsersTable} u ON p.user_id = u.id
                    ORDER BY p.created_at DESC", conn);

                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    listings.Add(new
                    {
                        id = reader.GetInt64("id"),
                        user_id = reader.IsDBNull(reader.GetOrdinal("user_id")) ? (int?)null : reader.GetInt32("user_id"),
                        name = reader.GetString("name"),
                        type = reader.IsDBNull(reader.GetOrdinal("type")) ? null : reader.GetString("type"),
                        price = reader.IsDBNull(reader.GetOrdinal("price")) ? (decimal?)null : reader.GetDecimal("price"),
                        location_lat = reader.IsDBNull(reader.GetOrdinal("location_lat")) ? (double?)null : reader.GetDouble("location_lat"),
                        location_lng = reader.IsDBNull(reader.GetOrdinal("location_lng")) ? (double?)null : reader.GetDouble("location_lng"),
                        map_data = reader.IsDBNull(reader.GetOrdinal("map_data")) ? "{}" : reader.GetString("map_data"),
                        created_at = reader.GetDateTime("created_at"),
                        owner_name = reader.IsDBNull(reader.GetOrdinal("username")) ? "Unknown" : reader.GetString("username"),
                        interest_count = reader.IsDBNull(reader.GetOrdinal("interest_count")) ? 0 : reader.GetInt32("interest_count")
                    });
                }

                return Ok(listings);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                return StatusCode(500, new { error = "Failed to list listings" });
            }
        }

        [HttpGet("user/{userId}")]
        public IActionResult GetUserTrips(int userId)
        {
            if (userId <= 0)
                return BadRequest(new { error = "Invalid user ID" });

            try
            {
                var listings = new List<object>();

                using var conn = new MySqlConnection(_configuration.GetConnectionString("DefaultConnection"));
                conn.Open();

                using var cmd = new MySqlCommand($@"
                    SELECT p.id,
                           p.user_id,
                           p.name,
                           p.type,
                           p.price,
                           p.location_lat,
                           p.location_lng,
                           p.map_data,
                           p.created_at,
                           u.username,
                           (SELECT COUNT(*) FROM trip_interests WHERE trip_id = p.id) as interest_count
                    FROM {ListingTable} p
                    LEFT JOIN {UsersTable} u ON p.user_id = u.id
                    WHERE p.user_id = @user_id
                    ORDER BY p.created_at DESC", conn);
                cmd.Parameters.AddWithValue("@user_id", userId);

                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    listings.Add(new
                    {
                        id = reader.GetInt64("id"),
                        user_id = reader.IsDBNull(reader.GetOrdinal("user_id")) ? (int?)null : reader.GetInt32("user_id"),
                        name = reader.GetString("name"),
                        type = reader.IsDBNull(reader.GetOrdinal("type")) ? null : reader.GetString("type"),
                        price = reader.IsDBNull(reader.GetOrdinal("price")) ? (decimal?)null : reader.GetDecimal("price"),
                        location_lat = reader.IsDBNull(reader.GetOrdinal("location_lat")) ? (double?)null : reader.GetDouble("location_lat"),
                        location_lng = reader.IsDBNull(reader.GetOrdinal("location_lng")) ? (double?)null : reader.GetDouble("location_lng"),
                        map_data = reader.IsDBNull(reader.GetOrdinal("map_data")) ? "{}" : reader.GetString("map_data"),
                        created_at = reader.GetDateTime("created_at"),
                        owner_name = reader.IsDBNull(reader.GetOrdinal("username")) ? "Unknown" : reader.GetString("username"),
                        interest_count = reader.IsDBNull(reader.GetOrdinal("interest_count")) ? 0 : reader.GetInt32("interest_count")
                    });
                }

                return Ok(listings);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                return StatusCode(500, new { error = "Failed to load user listings" });
            }
        }

        [HttpPatch("{tripId}/privacy")]
        public IActionResult UpdateTripPrivacy(int tripId, [FromBody] UpdatePrivacyDto dto)
        {
            return StatusCode(StatusCodes.Status501NotImplemented, new { error = "Listing privacy is not supported on the current postedadd schema." });
        }

        [HttpDelete("{id}")]
        public IActionResult DeleteTrip(int id)
        {
            if (id <= 0)
                return BadRequest(new { error = "Invalid listing ID" });

            try
            {
                using var conn = new MySqlConnection(_configuration.GetConnectionString("DefaultConnection"));
                conn.Open();

                using var checkCmd = new MySqlCommand($"SELECT id FROM {ListingTable} WHERE id = @id", conn);
                checkCmd.Parameters.AddWithValue("@id", id);
                var exists = checkCmd.ExecuteScalar();

                if (exists == null)
                    return NotFound(new { error = "Listing not found" });

                using var cmd = new MySqlCommand($"DELETE FROM {ListingTable} WHERE id = @id", conn);
                cmd.Parameters.AddWithValue("@id", id);
                cmd.ExecuteNonQuery();

                return Ok(new { message = "Listing deleted successfully" });
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                return StatusCode(500, new { error = "Failed to delete listing" });
            }
        }

        [HttpPost("{tripId}/interest")]
        public IActionResult RegisterInterest(int tripId, [FromBody] InterestRequest request)
        {
            if (tripId <= 0)
                return BadRequest(new { error = "Invalid trip ID" });

            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            try
            {
                using var conn = new MySqlConnection(_configuration.GetConnectionString("DefaultConnection"));
                conn.Open();
                
                // Check if listing exists
                using var checkTripCmd = new MySqlCommand($"SELECT id FROM {ListingTable} WHERE id = @id", conn);
                checkTripCmd.Parameters.AddWithValue("@id", tripId);
                var tripExists = checkTripCmd.ExecuteScalar();
                
                if (tripExists == null)
                    return NotFound(new { message = "Listing not found" });
                
                // Check if user exists
                using var checkUserCmd = new MySqlCommand($"SELECT id FROM {UsersTable} WHERE id = @id", conn);
                checkUserCmd.Parameters.AddWithValue("@id", request.UserId);
                var userExists = checkUserCmd.ExecuteScalar();
                
                if (userExists == null)
                    return BadRequest(new { message = "Invalid user" });
                
                // Register interest (or update timestamp if already exists)
                using var cmd = new MySqlCommand(@"
                    INSERT INTO trip_interests (trip_id, user_id) 
                    VALUES (@tripId, @userId)
                    ON DUPLICATE KEY UPDATE created_at = CURRENT_TIMESTAMP", conn);
                cmd.Parameters.AddWithValue("@tripId", tripId);
                cmd.Parameters.AddWithValue("@userId", request.UserId);
                cmd.ExecuteNonQuery();
                
                return Ok(new { message = "Interest registered successfully" });
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                return StatusCode(500, new { message = "Failed to register interest" });
            }
        }

        [HttpGet("{tripId}/interests")]
        public IActionResult GetTripInterests(int tripId)
        {
            if (tripId <= 0)
                return BadRequest(new { error = "Invalid trip ID" });

            try
            {
                using var conn = new MySqlConnection(_configuration.GetConnectionString("DefaultConnection"));
                conn.Open();
                
                using var cmd = new MySqlCommand(@"
                    SELECT u.id, u.username, u.email, ti.created_at
                    FROM trip_interests ti
                    JOIN userssl u ON ti.user_id = u.id
                    WHERE ti.trip_id = @tripId
                    ORDER BY ti.created_at DESC", conn);
                cmd.Parameters.AddWithValue("@tripId", tripId);
                
                var interests = new List<object>();
                using var reader = cmd.ExecuteReader();
                
                while (reader.Read())
                {
                    interests.Add(new
                    {
                        userId = reader.GetInt32("id"),
                        username = reader.GetString("username"),
                        email = reader.GetString("email"),
                        interestedAt = reader.GetDateTime("created_at")
                    });
                }
                
                return Ok(interests);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                return StatusCode(500, new { error = "Failed to load trip interests" });
            }
        }

        private string? ExtractFromMapData(JsonElement mapData, string field)
        {
            try
            {
                if (mapData.ValueKind == JsonValueKind.Null || mapData.ValueKind == JsonValueKind.Undefined)
                    return null;
                    
                if (mapData.TryGetProperty(field, out JsonElement value))
                {
                    if (value.ValueKind == JsonValueKind.String)
                        return value.GetString();
                    if (value.ValueKind == JsonValueKind.Object && value.TryGetProperty("name", out JsonElement nameValue))
                        return nameValue.GetString();
                }
                
                return null;
            }
            catch
            {
                return null;
            }
        }

        private double? ExtractDistanceFromMapData(JsonElement mapData)
        {
            try
            {
                if (mapData.ValueKind == JsonValueKind.Null || mapData.ValueKind == JsonValueKind.Undefined)
                    return null;
                    
                if (mapData.TryGetProperty("distance", out JsonElement distValue))
                {
                    if (distValue.ValueKind == JsonValueKind.Number)
                        return distValue.GetDouble();
                }
                
                if (mapData.TryGetProperty("distanceKm", out JsonElement distKmValue))
                {
                    if (distKmValue.ValueKind == JsonValueKind.Number)
                        return distKmValue.GetDouble();
                }
                
                if (mapData.TryGetProperty("distance_km", out JsonElement distKm2Value))
                {
                    if (distKm2Value.ValueKind == JsonValueKind.Number)
                        return distKm2Value.GetDouble();
                }
                
                return null;
            }
            catch
            {
                return null;
            }
        }
    }

    public class CreateTripRequest
    {
        [JsonPropertyName("user_id")]
        [Required(ErrorMessage = "user_id is required")]
        [Range(1, int.MaxValue, ErrorMessage = "user_id must be a positive number")]
        public int UserId { get; set; }

        [JsonPropertyName("name")]
        [Required(ErrorMessage = "name is required")]
        [StringLength(200, MinimumLength = 1, ErrorMessage = "name must be between 1 and 200 characters")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("type")]
        [Required(ErrorMessage = "type is required")]
        public string Type { get; set; } = "floor";

        [JsonPropertyName("price")]
        [Required(ErrorMessage = "price is required")]
        public decimal Price { get; set; }

        [JsonPropertyName("location")]
        public LocationPoint Location { get; set; } = new();

        [JsonPropertyName("map_data")]
        public string MapData { get; set; } = string.Empty;

        [JsonPropertyName("created_at")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    public class LocationPoint
    {
        [JsonPropertyName("lat")]
        public double Lat { get; set; }

        [JsonPropertyName("lng")]
        public double Lng { get; set; }
    }

    public class TripResponseDto
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }
        
        [JsonPropertyName("userId")]
        public int? UserId { get; set; }
        
        [JsonPropertyName("name")]
        public string? Name { get; set; }
        
        [JsonPropertyName("origin")]
        public string? Origin { get; set; }
        
        [JsonPropertyName("destination")]
        public string? Destination { get; set; }
        
        [JsonPropertyName("middleWaypoint")]
        public string? MiddleWaypoint { get; set; }
        
        [JsonPropertyName("ownerName")]
        public string? OwnerName { get; set; }
        
        [JsonPropertyName("distanceKm")]
        public double? DistanceKm { get; set; }
        
        [JsonPropertyName("isPublic")]
        public bool IsPublic { get; set; }
        
        [JsonPropertyName("map_data")]
        public object? MapData { get; set; }
        
        [JsonPropertyName("dateIso")]
        public DateTime DateIso { get; set; }
        
        [JsonPropertyName("interestCount")]
        public int InterestCount { get; set; }
    }

    public class UpdatePrivacyDto
    {
        [JsonPropertyName("isPublic")]
        [Required(ErrorMessage = "isPublic field is required")]
        public bool IsPublic { get; set; }
    }

    public class InterestRequest
    {
        [JsonPropertyName("userId")]
        [Required(ErrorMessage = "User ID is required")]
        [Range(1, int.MaxValue, ErrorMessage = "User ID must be a positive number")]
        public int UserId { get; set; }
    }
}
