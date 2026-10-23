using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace SmartDrive.Api.Infrastructure;

/// <summary>Adds the JWT bearer scheme to the OpenAPI document so Swagger UI shows an "Authorize" button.</summary>
internal sealed class BearerSecuritySchemeTransformer(IAuthenticationSchemeProvider schemes) : IOpenApiDocumentTransformer
{
    public async Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        var all = await schemes.GetAllSchemesAsync();
        if (!all.Any(s => s.Name == "Bearer"))
        {
            return;
        }

        document.Info.Title = "SmartDrive API";
        document.Info.Description =
            "REST API for SmartDrive, a car rental management system. Sign in with POST /api/auth/login, then click " +
            "Authorize and paste the token. Prices are in South African rand (ZAR); times use +02:00 (SAST).";

        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes = new Dictionary<string, IOpenApiSecurityScheme>
        {
            ["Bearer"] = new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                In = ParameterLocation.Header,
                BearerFormat = "JWT",
                Description = "JWT access token from POST /api/auth/login",
            },
        };
        document.Security =
        [
            new OpenApiSecurityRequirement { [new OpenApiSecuritySchemeReference("Bearer", document)] = [] },
        ];
    }
}
