namespace CartService.Infrastructure.Persistence;

using System;
using System.Threading;
using System.Threading.Tasks;
using CartService.Application.Abstractions;
using CartService.Domain;
using Microsoft.EntityFrameworkCore;

internal sealed class CartRepository : ICartRepository
{
    private readonly CartDbContext _context;

    public CartRepository(CartDbContext context)
    {
        _context = context;
    }

    public async Task<Cart?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        return await _context.Carts.FirstOrDefaultAsync(cart => cart.Id == id, cancellationToken);
    }

    public async Task<Cart?> GetActiveByCustomerAsync(Guid customerId, CancellationToken cancellationToken)
    {
        return await _context.Carts.FirstOrDefaultAsync(
            cart => cart.CustomerId == customerId && cart.Status == CartStatus.Active,
            cancellationToken);
    }

    public async Task<Cart?> GetByGuestTokenHashAsync(string guestTokenHash, CancellationToken cancellationToken)
    {
        return await _context.Carts.FirstOrDefaultAsync(cart => cart.GuestTokenHash == guestTokenHash, cancellationToken);
    }

    public void Add(Cart cart)
    {
        _context.Carts.Add(cart);
    }
}
