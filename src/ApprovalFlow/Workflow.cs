using Microsoft.EntityFrameworkCore;
namespace ApprovalFlow;

public sealed class Workflow(AppDb db)
{
    public static string? Validate(RequestInput input)
    {
        if (string.IsNullOrWhiteSpace(input.Title) || input.Title.Trim().Length > 100) return "件名は1〜100文字で入力してください。";
        if (string.IsNullOrWhiteSpace(input.Purpose) || input.Purpose.Trim().Length > 2000) return "目的は1〜2000文字で入力してください。";
        if (input.Amount is < 1 or > 100_000_000) return "金額は1〜100,000,000円で入力してください。";
        if (input.Title.Any(char.IsControl) || input.Purpose.Any(c => char.IsControl(c) && c != '\n' && c != '\r')) return "使用できない制御文字が含まれています。";
        return null;
    }
    public async Task<bool> Edit(Request row, RequestInput input, int actor, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var changed = await db.Requests.Where(x => x.Id == row.Id && x.OwnerId == actor && x.Version == input.Version && (x.Status == "Draft" || x.Status == "Returned"))
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Title, input.Title!.Trim()).SetProperty(x => x.Purpose, input.Purpose!.Trim()).SetProperty(x => x.Amount, input.Amount).SetProperty(x => x.Version, Guid.NewGuid()), ct);
        if (changed == 0) return false;
        db.Audits.Add(new Audit { RequestId = row.Id, ActorId = actor, Action = "Edited" });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return true;
    }
    public async Task<bool> Transition(Request row, DecisionInput input, int actor, string role, CancellationToken ct)
    {
        var next = input.Action switch
        {
            "submit" when row.OwnerId == actor && role == "Requester" && row.Status is "Draft" or "Returned" => "Submitted",
            "approve" when role == "Approver" && row.OwnerId != actor && row.Status == "Submitted" => "Approved",
            "return" when role == "Approver" && row.OwnerId != actor && row.Status == "Submitted" => "Returned",
            _ => null
        };
        if (next is null) return false;
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        // Compare-and-swap prevents a stale editor or a second approver from overwriting a decision.
        var changed = await db.Requests.Where(x => x.Id == row.Id && x.Version == input.Version && x.Status == row.Status)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, next).SetProperty(x => x.Version, Guid.NewGuid()), ct);
        if (changed == 0) return false;
        db.Audits.Add(new Audit { RequestId = row.Id, ActorId = actor, Action = next, Comment = input.Comment?.Trim() ?? "" });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return true;
    }
}
