using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using MySql.Data.MySqlClient;
using System.Data;
using System.ComponentModel.DataAnnotations;

namespace CSharpBackend.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [EnableRateLimiting("sliding")]
    public class UserController : ControllerBase
    {
        private readonly IConfiguration _configuration;
        public UserController(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        [HttpPost("register")]
        [EnableRateLimiting("token")] // Limit registration attempts
        public IActionResult Register([FromBody] RegisterDto dto)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            // Sanitize inputs
            dto.Username = dto.Username.Trim();
            dto.Email = dto.Email.Trim().ToLowerInvariant();
            dto.Password = dto.Password.Trim();

            using var conn = new MySqlConnection(_configuration.GetConnectionString("DefaultConnection"));
            conn.Open();
            using var checkCmd = new MySqlCommand("SELECT id FROM users WHERE email = @email", conn);
            checkCmd.Parameters.AddWithValue("@email", dto.Email);
            using var reader = checkCmd.ExecuteReader();
            if (reader.HasRows)
                return Conflict(new { message = "Email already registered" });
            reader.Close();

            using var cmd = new MySqlCommand("INSERT INTO users (username, email, password) VALUES (@username, @email, @password)", conn);
            cmd.Parameters.AddWithValue("@username", dto.Username);
            cmd.Parameters.AddWithValue("@email", dto.Email);
            cmd.Parameters.AddWithValue("@password", dto.Password);
            cmd.ExecuteNonQuery();
            return StatusCode(201, new { message = "User registered successfully" });
        }

        [HttpPost("login")]
        [EnableRateLimiting("token")] // Prevent brute force login attempts
        public IActionResult Login([FromBody] LoginDto dto)
        {
            if (!ModelState.IsValid)
            {
                Console.WriteLine("Login validation failed:");
                foreach (var error in ModelState.Values.SelectMany(v => v.Errors))
                {
                    Console.WriteLine($"  - {error.ErrorMessage}");
                }
                return BadRequest(ModelState);
            }

            // Sanitize inputs
            dto.Email = dto.Email.Trim().ToLowerInvariant();
            dto.Password = dto.Password.Trim();

            using var conn = new MySqlConnection(_configuration.GetConnectionString("DefaultConnection"));
            conn.Open();
            using var cmd = new MySqlCommand("SELECT id, username, email FROM users WHERE email = @email AND password = @password", conn);
            cmd.Parameters.AddWithValue("@email", dto.Email);
            cmd.Parameters.AddWithValue("@password", dto.Password);
            using var reader = cmd.ExecuteReader();
            if (!reader.Read())
                return Unauthorized(new { message = "Invalid credentials" });
            var user = new { id = reader.GetInt32(0), username = reader.GetString(1), email = reader.GetString(2) };
            return Ok(new { message = "Login successful", user });
        }

        [HttpDelete("{userId}")]
        public IActionResult DeleteAccount(int userId)
        {
            if (userId <= 0)
                return BadRequest(new { error = "Invalid user ID" });

            try
            {
                using var conn = new MySqlConnection(_configuration.GetConnectionString("DefaultConnection"));
                conn.Open();
                
                // Check if user exists
                using var checkCmd = new MySqlCommand("SELECT id FROM users WHERE id = @id", conn);
                checkCmd.Parameters.AddWithValue("@id", userId);
                var exists = checkCmd.ExecuteScalar();
                
                if (exists == null)
                    return NotFound(new { error = "User not found" });
                
                // Delete user (cascade delete will remove trips and trip_interests)
                using var cmd = new MySqlCommand("DELETE FROM users WHERE id = @id", conn);
                cmd.Parameters.AddWithValue("@id", userId);
                cmd.ExecuteNonQuery();
                
                return Ok(new { message = "Account deleted successfully" });
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                return StatusCode(500, new { error = "Failed to delete account" });
            }
        }
    }

    public class RegisterDto
    {
        [Required(ErrorMessage = "Username is required")]
        [StringLength(50, MinimumLength = 2, ErrorMessage = "Username must be between 2 and 50 characters")]
        public required string Username { get; set; }
        
        [Required(ErrorMessage = "Email is required")]
        [EmailAddress(ErrorMessage = "Invalid email format")]
        [StringLength(100, ErrorMessage = "Email cannot exceed 100 characters")]
        public required string Email { get; set; }
        
        [Required(ErrorMessage = "Password is required")]
        [StringLength(100, MinimumLength = 4, ErrorMessage = "Password must be at least 4 characters")]
        public required string Password { get; set; }
    }
    public class LoginDto
    {
        [Required(ErrorMessage = "Email is required")]
        public required string Email { get; set; }
        
        [Required(ErrorMessage = "Password is required")]
        public required string Password { get; set; }
    }
}
