using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using MySql.Data.MySqlClient;
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
        private readonly IConfiguration _configuration;
        
        public TripsController(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        [HttpPost]
        [EnableRateLimiting("token")] 
        public IActionResult SaveTrip([FromBody] TripDto dto)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            try
            {
                dto.Name = dto.Name?.Trim();
                
                if (dto.UserId.HasValue && dto.UserId.Value <= 0)
                    return BadRequest(new { error = "Invalid user_id" });

                using var conn = new MySqlConnection(_configuration.GetConnectionString("DefaultConnection"));
                conn.Open();
                
                string mapDataJson = dto.MapData != null ? JsonSerializer.Serialize(dto.MapData) : "{}";
                
                using var cmd = new MySqlCommand(
                    @"INSERT INTO trips (user_id, name, map_data, is_public, origin, destination, middleWaypoint, distanceKm) 
                      VALUES (@user_id, @name, @map_data, @is_public, @origin, @destination, @middleWaypoint, @distanceKm)", 
                    conn);
                cmd.Parameters.AddWithValue("@user_id", (object?)dto.UserId ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@name", dto.Name ?? string.Empty);
                cmd.Parameters.AddWithValue("@map_data", mapDataJson);
                cmd.Parameters.AddWithValue("@is_public", dto.IsPublic ?? true);
                cmd.Parameters.AddWithValue("@origin", (object?)dto.Origin ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@destination", (object?)dto.Destination ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@middleWaypoint", (object?)dto.MiddleWaypoint ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@distanceKm", (object?)dto.DistanceKm ?? DBNull.Value);
                
                cmd.ExecuteNonQuery();
                long insertId = cmd.LastInsertedId;
                
                return StatusCode(201, new { id = insertId });
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                return StatusCode(500, new { error = "Failed to save trip" });
            }
        }

        [HttpGet("{id}")]
        public IActionResult GetTrip(int id)
        {
            if (id <= 0)
                return BadRequest(new { error = "Invalid trip ID" });

            try
            {
                using var conn = new MySqlConnection(_configuration.GetConnectionString("DefaultConnection"));
                conn.Open();
                
                using var cmd = new MySqlCommand(@"
                    SELECT t.*, u.username,
                           (SELECT COUNT(*) FROM trip_interests WHERE trip_id = t.id) as interest_count
                    FROM trips t 
                    LEFT JOIN users u ON t.user_id = u.id 
                    WHERE t.id = @id", conn);
                cmd.Parameters.AddWithValue("@id", id);
                
                using var reader = cmd.ExecuteReader();
                if (!reader.Read())
                    return NotFound(new { error = "Trip not found" });
                
                var mapDataJson = reader.IsDBNull(reader.GetOrdinal("map_data")) 
                    ? "{}" 
                    : reader.GetString("map_data");
                var mapData = JsonSerializer.Deserialize<JsonElement>(mapDataJson);
                
                var trip = new TripResponseDto
                {
                    Id = reader.GetInt32("id"),
                    UserId = reader.IsDBNull(reader.GetOrdinal("user_id")) ? null : reader.GetInt32("user_id"),
                    Name = reader.GetString("name"),
                    Origin = reader.IsDBNull(reader.GetOrdinal("origin")) ? null : reader.GetString("origin"),
                    Destination = reader.IsDBNull(reader.GetOrdinal("destination")) ? null : reader.GetString("destination"),
                    MiddleWaypoint = reader.IsDBNull(reader.GetOrdinal("middleWaypoint")) ? null : reader.GetString("middleWaypoint"),
                    OwnerName = reader.IsDBNull(reader.GetOrdinal("username")) ? "Unknown" : reader.GetString("username"),
                    DistanceKm = reader.IsDBNull(reader.GetOrdinal("distanceKm")) ? null : (double)reader.GetDecimal("distanceKm"),
                    MapData = mapData,
                    IsPublic = reader.IsDBNull(reader.GetOrdinal("is_public")) ? true : reader.GetBoolean("is_public"),
                    DateIso = reader.GetDateTime("created_at"),
                    InterestCount = reader.GetInt32("interest_count")
                };
                
                return Ok(trip);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                return StatusCode(500, new { error = "Failed to load trip" });
            }
        }

        [HttpGet]
        public IActionResult GetAllTrips()
        {
            try
            {
                var trips = new List<TripResponseDto>();
                
                using var conn = new MySqlConnection(_configuration.GetConnectionString("DefaultConnection"));
                conn.Open();
                
                using var cmd = new MySqlCommand(@"
                    SELECT t.*, u.username,
                           (SELECT COUNT(*) FROM trip_interests WHERE trip_id = t.id) as interest_count
                    FROM trips t 
                    LEFT JOIN users u ON t.user_id = u.id 
                    WHERE t.is_public = 1 OR t.is_public IS NULL
                    ORDER BY t.created_at DESC", conn);
                using var reader = cmd.ExecuteReader();
                
                while (reader.Read())
                {
                    var mapDataJson = reader.IsDBNull(reader.GetOrdinal("map_data")) 
                        ? "{}" 
                        : reader.GetString("map_data");
                    var mapData = JsonSerializer.Deserialize<JsonElement>(mapDataJson);
                    
                    trips.Add(new TripResponseDto
                    {
                        Id = reader.GetInt32("id"),
                        UserId = reader.IsDBNull(reader.GetOrdinal("user_id")) ? null : reader.GetInt32("user_id"),
                        Name = reader.GetString("name"),
                        Origin = reader.IsDBNull(reader.GetOrdinal("origin")) ? null : reader.GetString("origin"),
                        Destination = reader.IsDBNull(reader.GetOrdinal("destination")) ? null : reader.GetString("destination"),
                        MiddleWaypoint = reader.IsDBNull(reader.GetOrdinal("middleWaypoint")) ? null : reader.GetString("middleWaypoint"),
                        OwnerName = reader.IsDBNull(reader.GetOrdinal("username")) ? "Unknown" : reader.GetString("username"),
                        DistanceKm = reader.IsDBNull(reader.GetOrdinal("distanceKm")) ? null : (double)reader.GetDecimal("distanceKm"),
                        MapData = mapData,
                        IsPublic = reader.IsDBNull(reader.GetOrdinal("is_public")) ? true : reader.GetBoolean("is_public"),
                        DateIso = reader.GetDateTime("created_at"),
                        InterestCount = reader.GetInt32("interest_count")
                    });
                }
                
                return Ok(trips);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                return StatusCode(500, new { error = "Failed to list trips" });
            }
        }

        [HttpGet("user/{userId}")]
        public IActionResult GetUserTrips(int userId)
        {
            if (userId <= 0)
                return BadRequest(new { error = "Invalid user ID" });

            try
            {
                var trips = new List<TripResponseDto>();
                
                using var conn = new MySqlConnection(_configuration.GetConnectionString("DefaultConnection"));
                conn.Open();
                
                using var cmd = new MySqlCommand(@"
                    SELECT t.*, u.username,
                           (SELECT COUNT(*) FROM trip_interests WHERE trip_id = t.id) as interest_count
                    FROM trips t 
                    LEFT JOIN users u ON t.user_id = u.id 
                    WHERE t.user_id = @user_id 
                    ORDER BY t.created_at DESC", conn);
                cmd.Parameters.AddWithValue("@user_id", userId);
                using var reader = cmd.ExecuteReader();
                
                while (reader.Read())
                {
                    var mapDataJson = reader.IsDBNull(reader.GetOrdinal("map_data")) 
                        ? "{}" 
                        : reader.GetString("map_data");
                    var mapData = JsonSerializer.Deserialize<JsonElement>(mapDataJson);
                    
                    trips.Add(new TripResponseDto
                    {
                        Id = reader.GetInt32("id"),
                        UserId = reader.IsDBNull(reader.GetOrdinal("user_id")) ? null : reader.GetInt32("user_id"),
                        Name = reader.GetString("name"),
                        Origin = reader.IsDBNull(reader.GetOrdinal("origin")) ? null : reader.GetString("origin"),
                        Destination = reader.IsDBNull(reader.GetOrdinal("destination")) ? null : reader.GetString("destination"),
                        MiddleWaypoint = reader.IsDBNull(reader.GetOrdinal("middleWaypoint")) ? null : reader.GetString("middleWaypoint"),
                        OwnerName = reader.IsDBNull(reader.GetOrdinal("username")) ? "Unknown" : reader.GetString("username"),
                        DistanceKm = reader.IsDBNull(reader.GetOrdinal("distanceKm")) ? null : (double)reader.GetDecimal("distanceKm"),
                        MapData = mapData,
                        IsPublic = reader.IsDBNull(reader.GetOrdinal("is_public")) ? true : reader.GetBoolean("is_public"),
                        DateIso = reader.GetDateTime("created_at"),
                        InterestCount = reader.GetInt32("interest_count")
                    });
                }
                
                return Ok(trips);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                return StatusCode(500, new { error = "Failed to load user trips" });
            }
        }

        [HttpPatch("{tripId}/privacy")]
        public IActionResult UpdateTripPrivacy(int tripId, [FromBody] UpdatePrivacyDto dto)
        {
            if (tripId <= 0)
                return BadRequest(new { error = "Invalid trip ID" });

            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            try
            {
                using var conn = new MySqlConnection(_configuration.GetConnectionString("DefaultConnection"));
                conn.Open();
                
                using var checkCmd = new MySqlCommand("SELECT id FROM trips WHERE id = @id", conn);
                checkCmd.Parameters.AddWithValue("@id", tripId);
                var exists = checkCmd.ExecuteScalar();
                
                if (exists == null)
                    return NotFound(new { error = "Trip not found" });
                
                using var cmd = new MySqlCommand("UPDATE trips SET is_public = @is_public WHERE id = @id", conn);
                cmd.Parameters.AddWithValue("@is_public", dto.IsPublic);
                cmd.Parameters.AddWithValue("@id", tripId);
                cmd.ExecuteNonQuery();
                
                return Ok(new { message = "Privacy setting updated successfully", isPublic = dto.IsPublic });
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                return StatusCode(500, new { error = "Failed to update privacy setting" });
            }
        }

        [HttpDelete("{id}")]
        public IActionResult DeleteTrip(int id)
        {
            if (id <= 0)
                return BadRequest(new { error = "Invalid trip ID" });

            try
            {
                using var conn = new MySqlConnection(_configuration.GetConnectionString("DefaultConnection"));
                conn.Open();
                
                using var checkCmd = new MySqlCommand("SELECT id FROM trips WHERE id = @id", conn);
                checkCmd.Parameters.AddWithValue("@id", id);
                var exists = checkCmd.ExecuteScalar();
                
                if (exists == null)
                    return NotFound(new { error = "Trip not found" });
                
                using var cmd = new MySqlCommand("DELETE FROM trips WHERE id = @id", conn);
                cmd.Parameters.AddWithValue("@id", id);
                cmd.ExecuteNonQuery();
                
                return Ok(new { message = "Trip deleted successfully" });
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                return StatusCode(500, new { error = "Failed to delete trip" });
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
                
                // Check if trip exists
                using var checkTripCmd = new MySqlCommand("SELECT id FROM trips WHERE id = @id", conn);
                checkTripCmd.Parameters.AddWithValue("@id", tripId);
                var tripExists = checkTripCmd.ExecuteScalar();
                
                if (tripExists == null)
                    return NotFound(new { message = "Trip not found" });
                
                // Check if user exists
                using var checkUserCmd = new MySqlCommand("SELECT id FROM users WHERE id = @id", conn);
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
                    JOIN users u ON ti.user_id = u.id
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

    public class TripDto
    {
        [JsonPropertyName("user_id")]
        [Range(1, int.MaxValue, ErrorMessage = "User ID must be a positive number")]
        public int? UserId { get; set; }
        
        [JsonPropertyName("name")]
        [Required(ErrorMessage = "Trip name is required")]
        [StringLength(200, MinimumLength = 1, ErrorMessage = "Trip name must be between 1 and 200 characters")]
        [RegularExpression(@"^[a-zA-Z0-9\s\-_.,!?()]+$", ErrorMessage = "Trip name contains invalid characters")]
        public string? Name { get; set; }
        
        [JsonPropertyName("map_data")]
        public object? MapData { get; set; }
        
        [JsonPropertyName("isPublic")]
        public bool? IsPublic { get; set; }
        
        [JsonPropertyName("origin")]
        public string? Origin { get; set; }
        
        [JsonPropertyName("destination")]
        public string? Destination { get; set; }
        
        [JsonPropertyName("middleWaypoint")]
        public string? MiddleWaypoint { get; set; }
        
        [JsonPropertyName("distanceKm")]
        public double? DistanceKm { get; set; }
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
