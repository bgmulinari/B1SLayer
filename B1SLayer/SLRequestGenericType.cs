using System.Collections.Generic;
using System.Threading.Tasks;

namespace B1SLayer;

/// <summary>
///     Represents a strongly-typed Service Layer request that enables type-safe LINQ expression support.
/// </summary>
/// <typeparam name="T">The type of the entity being requested from the Service Layer.</typeparam>
public class SLRequestGenericType<T>
{
    internal SLRequestGenericType(SLRequest request)
    {
        InnerRequest = request;
    }

    internal SLRequest InnerRequest { get; }

    /// <summary>
    ///     Gets the URL of the Service Layer request.
    /// </summary>
    public string Url => InnerRequest.Url;

    /// <summary>
    ///     Executes the request and returns a single entity of type T.
    /// </summary>
    /// <returns>A task that represents the asynchronous operation. The task result contains the requested entity.</returns>
    public async Task<T> GetSingleAsync()
    {
        return await InnerRequest.GetAsync<T>();
    }

    /// <summary>
    ///     Executes the request and returns a collection of entities of type T.
    /// </summary>
    /// <returns>
    ///     A task that represents the asynchronous operation. The task result contains the collection of requested
    ///     entities.
    /// </returns>
    public async Task<IEnumerable<T>> GetCollectionAsync()
    {
        return await InnerRequest.GetAsync<IEnumerable<T>>();
    }
}