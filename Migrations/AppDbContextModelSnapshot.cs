using System;
using cadll.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace cadll.Migrations
{
    [DbContext(typeof(AppDbContext))]
    partial class AppDbContextModelSnapshot : ModelSnapshot
    {
        protected override void BuildModel(ModelBuilder modelBuilder)
        {
#pragma warning disable 612, 618
            modelBuilder
                .HasAnnotation("ProductVersion", "9.0.15")
                .HasAnnotation("Relational:MaxIdentifierLength", 63);

            NpgsqlModelBuilderExtensions.UseIdentityByDefaultColumns(modelBuilder);

            modelBuilder.Entity("cadll.Data.Entities.AiApiCall", b =>
            {
                b.Property<Guid>("Id").ValueGeneratedOnAdd().HasColumnType("uuid");
                b.Property<string>("AiModel").IsRequired().HasMaxLength(64).HasColumnType("character varying(64)");
                b.Property<DateTime>("CalledAt").HasColumnType("timestamp with time zone");
                b.Property<int>("InputTokens").HasColumnType("integer");
                b.Property<Guid>("JobId").HasColumnType("uuid");
                b.Property<string>("Operation").IsRequired().HasMaxLength(32).HasColumnType("character varying(32)");
                b.Property<int>("OutputTokens").HasColumnType("integer");
                b.Property<string>("ResponseCode").HasColumnType("text");
                b.HasKey("Id");
                b.HasIndex("JobId");
                b.ToTable("AiApiCalls");
            });

            modelBuilder.Entity("cadll.Data.Entities.GenerationJob", b =>
            {
                b.Property<Guid>("Id").ValueGeneratedOnAdd().HasColumnType("uuid");
                b.Property<DateTime>("CreatedAt").HasColumnType("timestamp with time zone");
                b.Property<string>("FinalCode").HasColumnType("text");
                b.Property<string>("FunctionName").IsRequired().HasMaxLength(64).HasColumnType("character varying(64)");
                b.Property<string>("Outcome").IsRequired().HasMaxLength(32).HasColumnType("character varying(32)");
                b.Property<string>("Platform").IsRequired().HasMaxLength(32).HasColumnType("character varying(32)");
                b.Property<string>("Prompt").IsRequired().HasColumnType("text");
                b.Property<int>("TotalAiCalls").HasColumnType("integer");
                b.Property<int>("TotalInputTokens").HasColumnType("integer");
                b.Property<int>("TotalOutputTokens").HasColumnType("integer");
                b.Property<string>("UserIp").IsRequired().HasMaxLength(64).HasColumnType("character varying(64)");
                b.HasKey("Id");
                b.ToTable("GenerationJobs");
            });

            modelBuilder.Entity("cadll.Data.Entities.AiApiCall", b =>
            {
                b.HasOne("cadll.Data.Entities.GenerationJob", "Job")
                    .WithMany("AiApiCalls")
                    .HasForeignKey("JobId")
                    .OnDelete(DeleteBehavior.Cascade)
                    .IsRequired();
                b.Navigation("Job");
            });

            modelBuilder.Entity("cadll.Data.Entities.GenerationJob", b =>
            {
                b.Navigation("AiApiCalls");
            });
#pragma warning restore 612, 618
        }
    }
}
