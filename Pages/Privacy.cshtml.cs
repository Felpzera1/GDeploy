using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging; // Para ILogger<>
using System.Threading.Tasks;      // Para Task<>
using System.Collections.Generic;  // Para List<>
using Microsoft.AspNetCore.Hosting; // Para IWebHostEnvironmen

namespace GtopPdqNet.Pages;

public class PrivacyModel : PageModel
{
    private readonly ILogger<PrivacyModel> _logger;

    public PrivacyModel(ILogger<PrivacyModel> logger)
    {
        _logger = logger;
    }

    public void OnGet()
    {
    }
}

