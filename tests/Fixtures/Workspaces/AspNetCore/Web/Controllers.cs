using System.Data;
using Application;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Web;

// ASP001 (constructor persistence); authorization and abstraction are allowed.
[Authorize, ApiController, Route("orders")]
public class OrdersController(IDbConnection connection, IOrderService service) : ControllerBase
{
    [HttpGet] public IActionResult Get() => Ok();
    [HttpGet("open"), AllowAnonymous] public IActionResult Open() => Ok();
}

// ASP002: no declaration on the controller or its action.
public class OpenController : ControllerBase
{
    public override string ToString() => "not an action";

    [HttpGet("x")] public IActionResult Get() => Ok();
}

// ASP005: a concrete service from another project is injected.
[Authorize]
public class ConcreteController(ConcreteOrderService concrete) : ControllerBase
{
    [HttpGet("c")] public IActionResult Get() => Ok();
}

// ASP001 (body persistence): the persistence type appears only inside the action.
[Authorize]
public class BodyController : ControllerBase
{
    [HttpGet("b")] public IActionResult Query() => Ok(new DataTable().Rows.Count);

    [HttpGet("n"), NonAction] public IActionResult Hidden() => Ok();
}

// ASP002: an inherited action is unprotected on the concrete controller.
public abstract class BaseApiController : ControllerBase
{
    [HttpGet("inh")] public IActionResult Inherited() => Ok();

    [HttpGet("v")] public virtual IActionResult Replaced() => Ok();
}

// The override replaces the base action, so the route is reported once.
public class DerivedController : BaseApiController
{
    public override IActionResult Replaced() => Ok();
}
