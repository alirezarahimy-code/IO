using System;
using System.Collections.Generic;
using System.Linq;
using T5Web.Domain.Models;

namespace T5Web.Application.Services
{
    /// <summary>
    /// Builds the organisational chart and guards the one invariant the legacy portal
    /// did not check: the department tree must stay a tree.
    ///
    /// In the legacy T5V3Web a department could be dragged under its own grandchild
    /// (a cycle), which made every recursive report run until the request timed out.
    /// The stored procedure-free check below runs before any write.
    /// </summary>
    public sealed class DepartmentService
    {
        /// <summary>
        /// Turns the flat [Department] rows into a forest. Rows whose parent is
        /// missing (deleted parent, bad data) are attached to the root instead of
        /// disappearing - a person must always remain reachable in the chart.
        /// </summary>
        public IReadOnlyList<DepartmentNode> BuildTree(IEnumerable<DepartmentNode> flat)
        {
            if (flat == null)
            {
                return new List<DepartmentNode>();
            }

            var byId = new Dictionary<int, DepartmentNode>();
            foreach (DepartmentNode node in flat)
            {
                node.Children.Clear();
                byId[node.DepartmentId] = node;
            }

            var roots = new List<DepartmentNode>();
            foreach (DepartmentNode node in byId.Values.OrderBy(n => n.DepartmentName, StringComparer.CurrentCulture))
            {
                DepartmentNode parent;
                if (node.UpperDeptId <= 0 ||
                    node.UpperDeptId == node.DepartmentId ||
                    !byId.TryGetValue(node.UpperDeptId, out parent))
                {
                    roots.Add(node);
                    continue;
                }
                parent.Children.Add(node);
            }

            foreach (DepartmentNode root in roots)
            {
                AssignDepth(root, 1);
            }
            return roots;
        }

        private static void AssignDepth(DepartmentNode node, int depth)
        {
            node.Depth = depth;
            foreach (DepartmentNode child in node.Children)
            {
                AssignDepth(child, depth + 1);
            }
        }

        /// <summary>
        /// True when moving <paramref name="departmentId"/> under
        /// <paramref name="newParentId"/> would create a cycle or a self-parent.
        /// </summary>
        public bool WouldCreateCycle(IEnumerable<DepartmentNode> flat, int departmentId, int newParentId)
        {
            if (departmentId == newParentId)
            {
                return true;
            }
            if (newParentId <= 0)
            {
                return false;
            }
            if (flat == null)
            {
                return false;
            }

            var parentOf = flat.ToDictionary(n => n.DepartmentId, n => n.UpperDeptId);
            if (!parentOf.ContainsKey(newParentId))
            {
                return false; // new root-level department
            }

            // Walk up from the new parent; meeting the moved node means a cycle.
            int cursor = newParentId;
            var visited = new HashSet<int>();
            while (cursor > 0 && visited.Add(cursor))
            {
                if (cursor == departmentId)
                {
                    return true;
                }
                int next;
                if (!parentOf.TryGetValue(cursor, out next))
                {
                    break;
                }
                cursor = next;
            }
            return false;
        }

        /// <summary>
        /// Greatest depth in the forest. Used by the UI to warn before a tree becomes
        /// too deep for the report engine to render comfortably.
        /// </summary>
        public int MaxDepth(IEnumerable<DepartmentNode> roots)
        {
            if (roots == null)
            {
                return 0;
            }
            int max = 0;
            foreach (DepartmentNode node in roots)
            {
                max = Math.Max(max, DepthOf(node, 1));
            }
            return max;
        }

        private static int DepthOf(DepartmentNode node, int depth)
        {
            int max = depth;
            foreach (DepartmentNode child in node.Children)
            {
                max = Math.Max(max, DepthOf(child, depth + 1));
            }
            return max;
        }

        /// <summary>Total member count across all departments (for the dashboard).</summary>
        public int TotalMembers(IEnumerable<DepartmentNode> roots)
        {
            if (roots == null)
            {
                return 0;
            }
            return roots.SelectMany(r => r.Flatten()).Sum(n => n.MemberCount);
        }
    }
}
