/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FufuLauncher.Data.Migrations.Metadata;

/// <inheritdoc />
public partial class AddGachaPoolPresentation : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "BannerImageUrl",
            table: "GachaPoolMetadata",
            type: "TEXT",
            nullable: false,
            defaultValue: "");

        migrationBuilder.AddColumn<string>(
            name: "PoolName",
            table: "GachaPoolMetadata",
            type: "TEXT",
            nullable: false,
            defaultValue: "");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "BannerImageUrl",
            table: "GachaPoolMetadata");

        migrationBuilder.DropColumn(
            name: "PoolName",
            table: "GachaPoolMetadata");
    }
}
