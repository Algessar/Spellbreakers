using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;

public class CombatGraphExecutor
{
    public void Execute(List<GuardNode> nodes, float input)
    {
        var unprocessed = new HashSet<GuardNode>(nodes);
        var processed = new HashSet<GuardNode>();

        while (unprocessed.Any())
        {
            var readyNodes = unprocessed.Where
                (n => n.Dependencies.All
                    (d => processed.Contains(d))).ToList();

            if (readyNodes.Count == 0)
            {
                var stuckNodes = string.Join(", ", unprocessed.Select(n => n.Name));
                throw new Exception($"Graph has a dependency cycle! Stuck on: {stuckNodes}");
            }

            Parallel.ForEach(readyNodes, readyNode =>
            {
  
                var inputs = readyNode.Dependencies.ToDictionary(d => d, d => d.Output);
                readyNode.Output = readyNode.Work(inputs, input);
                Console.WriteLine($"[Executed] {readyNode.Name}");
                
            });

            foreach (var node in readyNodes)
            {
                unprocessed.Remove(node);
                processed.Add(node);
            }
        }
    }
}

public class CombatGraph
{
    private Dictionary<(string from, string to), string> transitionMap = new()
    {
        {("VomTag", "Alber"), "Langort"},
        {("VomTag","Ochs"),"Kron"} //VomTagOchsAdjust?
        
    };
}


public class GuardNode
{
    public string Name { get; set;}
    public List<GuardNode> Dependencies { get; set; } = new();
    
    
    public Func<Dictionary<GuardNode, object>, float, object> Work { get; set; }
    
    public object Output { get; set; }
}

public class Guard
{
    public string Name;

    public int _hash;

    public Guard(string name)
    {
        Name = name;
        _hash = Animator.StringToHash(Name);
    }

    public int Hash()
    {
        return _hash;
    }
}

public class PassingGuard
{
    
}