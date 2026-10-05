using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using MySqlConnector;
using System.Data;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace CSharpBackend.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [EnableRateLimiting("sliding")]
    public class ApiInfoController : ControllerBase
    {
        private readonly IConfiguration _configuration;
        public ApiInfoController(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        [HttpGet]
        public IActionResult GetAll()
        {
            var results = new List<ApiInfoDto>();
            using var conn = new MySqlConnection(_configuration.GetConnectionString("DefaultConnection"));
            conn.Open();
            using var cmd = new MySqlCommand("SELECT id, origin, destination, middleWaypoint, distanceKm, createdAtIso FROM api_info", conn);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                results.Add(new ApiInfoDto
                {
                    Id = reader.GetInt32(0),
                    Origin = reader.GetString(1),
                    Destination = reader.GetString(2),
                    MiddleWaypoint = reader.IsDBNull(3) ? null : reader.GetString(3),
                    DistanceKm = reader.IsDBNull(4) ? (decimal?)null : reader.GetDecimal(4),
                    CreatedAtIso = reader.GetString(5)
                });
            }
            return Ok(results);
        }

        [HttpPost]
        public IActionResult Create([FromBody] ApiInfoDto dto)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            try
            {
                dto.Origin = dto.Origin?.Trim();
                dto.Destination = dto.Destination?.Trim();
                dto.MiddleWaypoint = dto.MiddleWaypoint?.Trim();
                dto.CreatedAtIso = dto.CreatedAtIso?.Trim();

                if (dto.DistanceKm.HasValue && dto.DistanceKm.Value < 0)
                    return BadRequest(new { message = "Distance cannot be negative" });

                using var conn = new MySqlConnection(_configuration.GetConnectionString("DefaultConnection"));
                conn.Open();
                using var cmd = new MySqlCommand("INSERT INTO api_info (origin, destination, middleWaypoint, distanceKm, createdAtIso) VALUES (@origin, @destination, @middleWaypoint, @distanceKm, @createdAtIso)", conn);
                cmd.Parameters.AddWithValue("@origin", dto.Origin);
                cmd.Parameters.AddWithValue("@destination", dto.Destination);
                cmd.Parameters.AddWithValue("@middleWaypoint", (object?)dto.MiddleWaypoint ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@distanceKm", (object?)dto.DistanceKm ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@createdAtIso", dto.CreatedAtIso);
                cmd.ExecuteNonQuery();
                return StatusCode(201, new { message = "Route saved successfully" });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in Create: {ex.Message}\n{ex.StackTrace}");
                return StatusCode(500, new { message = "Internal server error", error = ex.Message, stackTrace = ex.StackTrace });
            }
        }
    }

    public class ApiInfoDto
    {
        public int Id { get; set; }
        
        [Required(ErrorMessage = "Origin is required")]
        [StringLength(200, ErrorMessage = "Origin cannot exceed 200 characters")]
        public string? Origin { get; set; }
        
        [Required(ErrorMessage = "Destination is required")]
        [StringLength(200, ErrorMessage = "Destination cannot exceed 200 characters")]
        public string? Destination { get; set; }
        
        [StringLength(200, ErrorMessage = "Middle waypoint cannot exceed 200 characters")]
        public string? MiddleWaypoint { get; set; }
        
        [Range(0, 999999.99, ErrorMessage = "Distance must be between 0 and 999999.99 km")]
        public decimal? DistanceKm { get; set; }
        
        [Required(ErrorMessage = "CreatedAtIso is required")]
        [RegularExpression(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(\.\d+)?Z?$", ErrorMessage = "Invalid ISO 8601 date format")]
        public string? CreatedAtIso { get; set; }
    }
}
