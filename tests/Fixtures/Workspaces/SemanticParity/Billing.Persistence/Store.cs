namespace Legacy.Data;

public class Store
{
    public static int Count => 0;
    public static Store operator +(Store left, Store right) => left;
}
public interface IStore { }
public class StoreAttribute : System.Attribute { }
public static class Extensions { public static Store Read(this string value) => new(); }
