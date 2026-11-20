namespace Application;

/// <summary>An application abstraction that endpoints may depend on.</summary>
public interface IOrderService { }

/// <summary>A concrete application service that endpoints must not depend on directly.</summary>
public class ConcreteOrderService : IOrderService { }
