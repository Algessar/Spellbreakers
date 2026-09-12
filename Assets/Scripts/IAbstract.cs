using UnityEditor.ShaderGraph.Internal;

using System.Data;

namespace DefaultNamespace
{
    public interface IAbstract
    {
        public int MyProperty { get; set; }
        
        public abstract int Abstract { get; set; }

        protected abstract int Protected { get; set; }

        public virtual void MyVirtualMethod()
        {
            
        }
    }
}