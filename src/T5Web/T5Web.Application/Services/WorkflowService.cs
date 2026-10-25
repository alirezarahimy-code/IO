using System;
using System.Collections.Generic;
using System.Linq;
using T5Web.Domain.Interfaces;
using T5Web.Domain.Models;

namespace T5Web.Application.Services
{
    /// <summary>
    /// Approval-chain rules for [Workflow].
    ///
    /// The legacy T5V3Web let an operator save a chain with two steps at the same
    /// OrderIndex, or with the requester's own title as the first accepter. Both
    /// produce requests that can never be resolved: the first blocks in the grid,
    /// the second auto-approves everything the person asks for.
    /// </summary>
    public sealed class WorkflowService
    {
        private readonly IAdminRepository _repository;

        public WorkflowService(IAdminRepository repository)
        {
            if (repository == null)
            {
                throw new ArgumentNullException(nameof(repository));
            }
            _repository = repository;
        }

        public IReadOnlyList<WorkflowStep> Get(int workflowType, int leaveTypeId)
        {
            return _repository.GetWorkflow(workflowType, leaveTypeId)
                .OrderBy(s => s.OrderIndex)
                .ToList();
        }

        /// <summary>Validates the whole chain before anything is written.</summary>
        public OperationResult Validate(IReadOnlyList<WorkflowStep> steps)
        {
            if (steps == null || steps.Count == 0)
            {
                return OperationResult.Fail("زنجیره تأیید نمی‌تواند خالی باشد.");
            }
            if (steps.Count > 10)
            {
                return OperationResult.Fail("حداکثر ۱۰ مرحله تأیید مجاز است.");
            }

            var orders = new HashSet<int>();
            foreach (WorkflowStep step in steps)
            {
                if (step.OrderIndex <= 0)
                {
                    return OperationResult.Fail("شماره مرحله باید بزرگ‌تر از صفر باشد.");
                }
                if (!orders.Add(step.OrderIndex))
                {
                    return OperationResult.Fail("شماره مرحله " + step.OrderIndex + " تکراری است.");
                }
                if (step.TitleId <= 0)
                {
                    return OperationResult.Fail("عنوان درخواست‌کننده مشخص نشده است.");
                }
                if (step.AccepterTitleId <= 0)
                {
                    return OperationResult.Fail("عنوان تأییدکننده مرحله " + step.OrderIndex + " مشخص نشده است.");
                }
                if (step.AccepterTitleId == step.TitleId)
                {
                    return OperationResult.Fail(
                        "عنوان تأییدکننده مرحله " + step.OrderIndex +
                        " با درخواست‌کننده یکسان است؛ این مرحله عملاً تأیید خودکار است.");
                }
            }

            // Order numbers must be a contiguous 1..n run; gaps confuse the engine's
            // step lookup, which walks the chain by index.
            var sorted = orders.OrderBy(x => x).ToList();
            for (int i = 0; i < sorted.Count; i++)
            {
                if (sorted[i] != i + 1)
                {
                    return OperationResult.Fail("شماره مراحل باید پیوسته و از ۱ شروع شود (مقدار " + sorted[i] + " نامعتبر است).");
                }
            }

            return OperationResult.Ok();
        }

        public OperationResult Save(IReadOnlyList<WorkflowStep> steps, int workflowType, int leaveTypeId)
        {
            OperationResult validation = Validate(steps);
            if (!validation.Succeeded)
            {
                return validation;
            }
            return _repository.SaveWorkflow(steps, workflowType, leaveTypeId);
        }
    }
}
