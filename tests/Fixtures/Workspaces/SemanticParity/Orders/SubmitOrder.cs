global using Alias = Legacy.Data.Store;
global using ArrayAlias = Legacy.Data.Store[];
global using GenericAlias = System.Collections.Generic.List<Legacy.Data.Store>;
using Legacy.Data;
using Unused = Legacy.Data.IStore; // case:unused-import

namespace Orders
{
    [Store] // case:attribute
    public partial class SubmitOrder<T> : Store, // case:base
        IStore // case:interface
        where T : Store // case:constraint
    {
        public Alias? Aliased; // case:alias
        public ArrayAlias? AliasedArray; // case:array-alias
        public GenericAlias? AliasedList; // case:generic-alias
        public System.Collections.Generic.List<Store[]>? Items; // case:generic-array
        public Billing.Contracts.IInvoice? Contract; // case:contract
        public Legacy.DataLike.Store? Lookalike; // case:lookalike
        public Legacy.Data.Allowed.Token? Exception; // case:exception
        public string Text = "Legacy.Data.Store"; // case:literal
        public object Create() => new Store(); // case:creation
        public System.Type Type() => typeof(Store); // case:typeof
        public string Name() => nameof(Store); // case:nameof
        public object Cast(object value) => (Store)value; // case:cast
        public bool Match(object value) => value is Store; // case:pattern
        public int Count() => Store.Count; // case:member
        public object Read() => "input".Read(); // case:extension
        public object Sum(Store left, Store right) // case:operator-signature
        {
            return left + right; // case:implicit-operator
        }
#if DEBUG
        public Store? DebugStore; // case:debug
#else
        public Billing.Contracts.IInvoice? ReleaseContract; // case:release
#endif
#if NET10_0
        public Store? ModernStore; // case:net10
#else
        public Billing.Contracts.IInvoice? PortableContract; // case:netstandard
#endif
    }
}

namespace Legacy.DataLike { public class Store { } }
namespace Legacy.Data.Allowed { public class Token { } }
