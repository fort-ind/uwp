using System.Threading.Tasks;

namespace Fort.ind_UWP
{
    internal interface IUnsavedChangesPage
    {
        bool HasUnsavedChanges { get; }

        Task<bool> ConfirmLeaveAsync();
    }
}
