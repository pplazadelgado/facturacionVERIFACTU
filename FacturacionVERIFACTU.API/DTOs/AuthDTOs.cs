using System.ComponentModel.DataAnnotations;

namespace FacturacionVERIFACTU.API.DTOs
{

    // ===== Request DTOs
    public class RegisterRequest
    {
        [Required(ErrorMessage ="El email es obligatorio")]
        [EmailAddress(ErrorMessage ="Email invalido")]
        public string Email { get; set; } = string.Empty;

        [Required(ErrorMessage = "La contraseña es obligatoria")]
        [MinLength(8, ErrorMessage = "Mínimo 8 caracteres")]
        [RegularExpression(@"^(?=.*[a-z])(?=.*[A-Z])(?=.*\d)(?=.*[@$!%*?&])[A-Za-z\d@$!%*?&]{8,}$",
         ErrorMessage = "La contraseña debe contener mayúsculas, minúsculas, números y caracteres especiales")]
        public string Password { get; set; } = string.Empty;

        [Required(ErrorMessage ="El nombre es obligatorio")]
        [MinLength(3,ErrorMessage ="Minimo 3 caracteres")]
        public string NombreCompleto { get; set; } = string.Empty;

        [Required (ErrorMessage ="El nombre de la empresa es obligatorio")]
        [MinLength(3,ErrorMessage ="Minimo 3 caracteres")]
        public string NombreEmpresa {  get; set; } = string.Empty ;

        [Required(ErrorMessage ="El NIF es obligatorio")]
        [RegularExpression(@"^[A-Z]\d{8}$|^\d{8}[A-Z]$",
            ErrorMessage = "NIF inválido (formato: A12345678 o 12345678A)")]
        public string NIF { get; set; } = string.Empty;
    }

    public class LoginRequest
    {
        [Required]
        [EmailAddress]
        public string Email { get; set; } = string.Empty;

        [Required]
        public string Password { get; set; } = string.Empty;
    }

    public class RefreshTokenRequest
    {
        [Required]
        public string RefreshToken {  get; set; } = string.Empty;
    }

    // ===== RESPONSE DTOs ====

    public class AuthResponse
    {
        public string AccessToken { get; set; } = string.Empty;
        public string RefreshToken { get; set; }= string.Empty;
        public DateTime ExpiresAt { get; set; }
        public UserInfo User { get; set; } = new();
    }

    public class UserInfo
    {
        public int UserId {  get; set; }
        public string Email { get; set; } = string.Empty;
        public string NombreCompleto { get; set; } = string.Empty;
        public int TenantId {  get; set; }
        public string NombreEmpresa { get; set; } = string.Empty;
        public string Role {  get; set; } = string.Empty;
    }

    public class ResetPasswordResponseDto
    {
        public bool Success { get; set; }
        public string Mensaje { get; set; } = string.Empty;
        public string? Email { get; set; }
        public string? PasswordTemporal { get; set; }
    }

    public class CambiarPasswordDto
    {
        [Required(ErrorMessage = "La contraseña actual es obligatoria")]
        public string CurrentPassword { get; set; } = string.Empty;

        [Required(ErrorMessage = "La nueva contraseña es obligatoria")]
        [MinLength(6, ErrorMessage = "La contraseña debe tener al menos 6 caracteres")]
        public string NewPassword { get; set; } = string.Empty;

        [Required(ErrorMessage = "Debe confirmar la nueva contraseña")]
        [Compare(nameof(NewPassword), ErrorMessage = "Las contraseñas no coinciden")]
        public string ConfirmarPassword { get; set; } = string.Empty;
    }

    // ===== RECUPERACIÓN DE CONTRASEÑA =====

    public class ForgotPasswordRequest
    {
        [Required(ErrorMessage = "El email es obligatorio")]
        [EmailAddress(ErrorMessage = "Email inválido")]
        public string Email { get; set; } = string.Empty;
    }

    public class ResetPasswordRequest
    {
        [Required(ErrorMessage = "El token es obligatorio")]
        public string Token { get; set; } = string.Empty;

        [Required(ErrorMessage = "La nueva contraseña es obligatoria")]
        [MinLength(8, ErrorMessage = "Mínimo 8 caracteres")]
        [RegularExpression(
            @"^(?=.*[a-z])(?=.*[A-Z])(?=.*\d)(?=.*[@$!%*?&])[A-Za-z\d@$!%*?&]{8,}$",
            ErrorMessage = "La contraseña debe contener mayúsculas, minúsculas, números y caracteres especiales")]
        public string NewPassword { get; set; } = string.Empty;

        [Required(ErrorMessage = "Debes confirmar la contraseña")]
        [Compare(nameof(NewPassword), ErrorMessage = "Las contraseñas no coinciden")]
        public string ConfirmPassword { get; set; } = string.Empty;
    }

    public class ForgotPasswordResponse
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
    }

}
