using HemenIlanVer.Domain.Entities;
using HemenIlanVer.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace HemenIlanVer.Infrastructure.Persistence;

public sealed class AppDbContext : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<City> Cities => Set<City>();
    public DbSet<District> Districts => Set<District>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<CategoryAttribute> CategoryAttributes => Set<CategoryAttribute>();
    public DbSet<CategoryAttributeOption> CategoryAttributeOptions => Set<CategoryAttributeOption>();
    public DbSet<Listing> Listings => Set<Listing>();
    public DbSet<ListingAttributeValue> ListingAttributeValues => Set<ListingAttributeValue>();
    public DbSet<ListingImage> ListingImages => Set<ListingImage>();
    public DbSet<Favorite> Favorites => Set<Favorite>();
    public DbSet<Conversation> Conversations => Set<Conversation>();
    public DbSet<Message> Messages => Set<Message>();
    public DbSet<AiExtractionLog> AiExtractionLogs => Set<AiExtractionLog>();
    public DbSet<SearchLog> SearchLogs => Set<SearchLog>();
    public DbSet<ModerationLog> ModerationLogs => Set<ModerationLog>();
    public DbSet<ListingEmbedding> ListingEmbeddings => Set<ListingEmbedding>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<ApplicationUser>(e =>
        {
            e.Property(x => x.DisplayName).HasMaxLength(200);
        });

        modelBuilder.Entity<RefreshToken>(e =>
        {
            e.ToTable("refresh_tokens");
            e.HasKey(x => x.Id);
            e.Property(x => x.Token).HasMaxLength(500).IsRequired();
            e.HasIndex(x => x.Token);
            e.HasIndex(x => x.UserId);
            e.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<City>(e =>
        {
            e.ToTable("cities");
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(120).IsRequired();
            e.Property(x => x.Slug).HasMaxLength(120).IsRequired();
            e.HasIndex(x => x.Slug).IsUnique();
        });

        modelBuilder.Entity<District>(e =>
        {
            e.ToTable("districts");
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(120).IsRequired();
            e.HasOne(x => x.City).WithMany(x => x.Districts).HasForeignKey(x => x.CityId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.CityId, x.Slug });
        });

        modelBuilder.Entity<Category>(e =>
        {
            e.ToTable("categories");
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.Property(x => x.Slug).HasMaxLength(200).IsRequired();
            e.HasIndex(x => x.Slug).IsUnique();
            e.HasOne(x => x.Parent).WithMany(x => x.Children).HasForeignKey(x => x.ParentId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<CategoryAttribute>(e =>
        {
            e.ToTable("category_attributes");
            e.HasKey(x => x.Id);
            e.Property(x => x.AttributeKey).HasMaxLength(80).IsRequired();
            e.Property(x => x.DisplayName).HasMaxLength(200).IsRequired();
            e.HasIndex(x => new { x.CategoryId, x.AttributeKey }).IsUnique();
            e.HasOne(x => x.Category).WithMany(x => x.Attributes).HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.ParentAttribute).WithMany().HasForeignKey(x => x.ParentAttributeId).OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<CategoryAttributeOption>(e =>
        {
            e.ToTable("category_attribute_options");
            e.HasKey(x => x.Id);
            e.Property(x => x.ValueKey).HasMaxLength(80).IsRequired();
            e.Property(x => x.Label).HasMaxLength(200).IsRequired();
            e.HasOne(x => x.CategoryAttribute).WithMany(x => x.Options).HasForeignKey(x => x.CategoryAttributeId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.ParentOption).WithMany().HasForeignKey(x => x.ParentOptionId).OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<Listing>(e =>
        {
            e.ToTable("listings");
            e.HasKey(x => x.Id);
            e.Property(x => x.Title).HasMaxLength(200).IsRequired();
            e.Property(x => x.Description).HasMaxLength(8000).IsRequired();
            e.Property(x => x.Currency).HasMaxLength(3).IsRequired();
            e.Property(x => x.Price).HasPrecision(18, 2);
            e.HasIndex(x => new { x.CategoryId, x.Status, x.CreatedAt });
            e.HasIndex(x => x.UserId);
            e.HasOne(x => x.Category).WithMany().HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.City).WithMany().HasForeignKey(x => x.CityId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.District).WithMany().HasForeignKey(x => x.DistrictId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ListingAttributeValue>(e =>
        {
            e.ToTable("listing_attribute_values");
            e.HasKey(x => x.Id);
            e.Property(x => x.ValueText).HasMaxLength(2000);
            e.Property(x => x.ValueDecimal).HasPrecision(18, 4);
            e.Property(x => x.ValueJson).HasMaxLength(4000);
            e.HasIndex(x => new { x.ListingId, x.CategoryAttributeId }).IsUnique();
            e.HasOne(x => x.Listing).WithMany(x => x.AttributeValues).HasForeignKey(x => x.ListingId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.CategoryAttribute).WithMany().HasForeignKey(x => x.CategoryAttributeId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ListingImage>(e =>
        {
            e.ToTable("listing_images");
            e.HasKey(x => x.Id);
            e.Property(x => x.Url).HasMaxLength(2000).IsRequired();
            e.HasOne(x => x.Listing).WithMany(x => x.Images).HasForeignKey(x => x.ListingId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Favorite>(e =>
        {
            e.ToTable("favorites");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.UserId, x.ListingId }).IsUnique();
            e.HasOne(x => x.Listing).WithMany().HasForeignKey(x => x.ListingId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Conversation>(e =>
        {
            e.ToTable("conversations");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.ListingId, x.BuyerUserId }).IsUnique();
            e.HasOne(x => x.Listing).WithMany().HasForeignKey(x => x.ListingId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.BuyerUserId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.SellerUserId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Message>(e =>
        {
            e.ToTable("messages");
            e.HasKey(x => x.Id);
            e.Property(x => x.Body).HasMaxLength(4000).IsRequired();
            e.HasIndex(x => new { x.ConversationId, x.CreatedAt });
            e.HasOne(x => x.Conversation).WithMany(x => x.Messages).HasForeignKey(x => x.ConversationId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.SenderUserId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<AiExtractionLog>(e =>
        {
            e.ToTable("ai_extraction_logs");
            e.HasKey(x => x.Id);
            e.Property(x => x.Kind).HasMaxLength(40).IsRequired();
            e.Property(x => x.Model).HasMaxLength(80);
            e.Property(x => x.Error).HasMaxLength(2000);
        });

        modelBuilder.Entity<SearchLog>(e =>
        {
            e.ToTable("search_logs");
            e.HasKey(x => x.Id);
            e.Property(x => x.RawQuery).HasMaxLength(2000).IsRequired();
        });

        modelBuilder.Entity<ModerationLog>(e =>
        {
            e.ToTable("moderation_logs");
            e.HasKey(x => x.Id);
            e.Property(x => x.Action).HasMaxLength(80).IsRequired();
            e.HasOne(x => x.Listing).WithMany().HasForeignKey(x => x.ListingId).OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<ListingEmbedding>(e =>
        {
            e.ToTable("listing_embeddings");
            e.HasKey(x => x.Id);
            e.Property(x => x.SearchableText).IsRequired();
            e.HasIndex(x => x.ListingId).IsUnique();
            e.HasOne(x => x.Listing).WithMany().HasForeignKey(x => x.ListingId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}
