using System.Text.RegularExpressions;
using Tenants.Api.Domain;

namespace Tenants.Api.Features.Members;

/// <summary>Demande d'ajout passée par <see cref="MemberValidation"/>.</summary>
public sealed record ValidatedCreateMember(
    string Username,
    string FirstName,
    string LastName,
    MemberRole Role,
    bool IsAdmin,
    string TemporaryPassword);

public static partial class MemberValidation
{
    public const int PasswordMinLength = 8;
    public const int PasswordMaxLength = 128;
    public const int NameMaxLength = 100;

    public static bool TryValidate(
        CreateMemberRequest request,
        out ValidatedCreateMember? validated,
        out Dictionary<string, string[]> errors)
    {
        errors = [];
        validated = null;

        // Keycloak range les identifiants en minuscules : on le fait avant
        // lui, pour que la recherche de doublon porte sur la même valeur.
        var username = request.Username?.Trim().ToLowerInvariant() ?? string.Empty;
        if (!UsernamePattern().IsMatch(username))
        {
            errors["username"] = ["3 à 50 caractères : lettres minuscules, chiffres, point, tiret ou souligné, en commençant par une lettre ou un chiffre."];
        }

        var firstName = request.FirstName?.Trim() ?? string.Empty;
        if (firstName.Length is 0 or > NameMaxLength)
        {
            errors["firstName"] = ["Le prénom est requis."];
        }

        var lastName = request.LastName?.Trim() ?? string.Empty;
        if (lastName.Length is 0 or > NameMaxLength)
        {
            errors["lastName"] = ["Le nom est requis."];
        }

        ValidateRole(request.Role, request.IsAdmin, errors);
        ValidatePassword(request.TemporaryPassword, errors);

        if (errors.Count > 0)
        {
            return false;
        }

        validated = new ValidatedCreateMember(
            username, firstName, lastName, request.Role!.Value, request.IsAdmin, request.TemporaryPassword!);
        return true;
    }

    public static void ValidateRole(MemberRole? role, bool isAdmin, Dictionary<string, string[]> errors)
    {
        if (role is not { } value || !Enum.IsDefined(value))
        {
            errors["role"] = ["Choisissez adulte ou enfant."];
        }
        else if (isAdmin && value != MemberRole.Adult)
        {
            // Administrer le ménage, c'est créer des comptes et réinitialiser
            // des mots de passe : un rôle d'adulte.
            errors["isAdmin"] = ["Seul un adulte peut administrer le ménage."];
        }
    }

    public static void ValidatePassword(string? password, Dictionary<string, string[]> errors)
    {
        if (password is null || password.Length < PasswordMinLength || password.Length > PasswordMaxLength
            || string.IsNullOrWhiteSpace(password))
        {
            errors["temporaryPassword"] = [$"Au moins {PasswordMinLength} caractères."];
        }
    }

    [GeneratedRegex("^[a-z0-9][a-z0-9._-]{2,49}$", RegexOptions.CultureInvariant)]
    private static partial Regex UsernamePattern();
}
