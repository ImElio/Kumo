using Kumo.Core.Files;
using Kumo.Core.Operations;
using Kumo.Core.Radial;

namespace Kumo.Operations.Tests;

public class RegistryIntegrationTests
{
    private static readonly OperationRegistry Registry = new(KumoOperations.CreateAll());

    private static IReadOnlyList<string> Root(params FileKind[] kinds) =>
        new RadialMenuModel(Registry.EntriesFor(kinds.Select((k, i) => new InputFile($"f{i}", k, 10)).ToList())).Items.Select(i => i.Label).ToList();

    [Fact]
    public void Png_offers_image_actions()
    {
        Assert.Equal(["Convert", "Crop", "Compress", "Rotate"], Root(FileKind.Png));
    }

    [Fact]
    public void Png_conversion_targets_exclude_png()
    {
        var model = new RadialMenuModel(Registry.EntriesFor([new InputFile("a", FileKind.Png, 1)]));
        model.Activate(0);
        Assert.Equal(["JPG", "WEBP", "BMP", "PDF"], model.Items.Select(i => i.Label));
    }

    [Fact]
    public void Several_images_can_be_combined_into_a_pdf()
    {
        Assert.Contains("Combine into PDF", Root(FileKind.Png, FileKind.Jpeg));
    }

    [Fact]
    public void Pdf_offers_document_actions()
    {
        Assert.Equal(["Convert", "Split", "Extract", "Protect"], Root(FileKind.Pdf));
    }

    [Fact]
    public void Two_pdfs_can_be_merged()
    {
        Assert.Contains("Merge PDFs", Root(FileKind.Pdf, FileKind.Pdf));
    }

    [Fact]
    public void Tiff_is_not_offered_anything_it_cannot_do()
    {
        Assert.Empty(Root(FileKind.Tiff));
    }

    [Fact]
    public void Mixed_images_and_pdfs_share_only_common_targets()
    {
        var model = new RadialMenuModel(Registry.EntriesFor([new InputFile("a", FileKind.Jpeg, 1), new InputFile("b", FileKind.Pdf, 1)]));
        Assert.Equal(["To PNG"], model.Items.Select(i => i.Label));
        Assert.Equal(2, model.Items[0].Entry!.Operations.Count);
    }

    [Fact]
    public void Operation_ids_are_unique()
    {
        var ids = KumoOperations.CreateAll().Select(o => o.Id).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());
    }
}
