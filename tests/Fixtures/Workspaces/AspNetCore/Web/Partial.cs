using System.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Web;

// ASP001: one controller declared in two parts; the action's use of the constructor dependency belongs to the controller.
[Authorize]
public partial class PartialController(IDbConnection connection) : ControllerBase { }

public partial class PartialController
{
    [HttpGet("p")] public IActionResult State() => Ok(connection.State);
}
