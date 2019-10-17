﻿using System;
using System.Collections.Generic;

namespace Roblox.Reflection
{
    public enum DiffType
    {
        Add    = 1,
        Merge  = 3,
        Change = 2,
        Remove = 4,
        Rename = 0,
    }

    public class Diff : IComparable
    {
        private const string NL = "\r\n";
        
        private List<Diff> children = new List<Diff>();
        private int stack;

        public DiffType Type;

        public string Field = "";
        public object Context;

        public Descriptor Target;

        public DiffChangeList From = new DiffChangeList("ChangeFrom");
        public DiffChangeList To = new DiffChangeList("ChangeTo");

        public bool HasParent => (stack > 0);
        public Diff[] Children => children.ToArray();

        public bool Detailed;
        public bool Disposed;
        
        public void AddChild(Diff child)
        {
            if (!children.Contains(child))
            {
                child.stack++;
                children.Add(child);
            }
        }

        public string WriteDiffTxt(bool detailed = false)
        {
            string result = "";

            for (int i = 0; i < stack; i++)
                result += '\t';

            string what = Target.Describe(detailed);

            if (Type != DiffType.Change)
                what = (what.StartsWith(Field) ? "" : Field + ' ') + what;
            else
                what = "the " + Field + " of " + what;

            switch (Type)
            {
                case DiffType.Add:
                    result += "Added " + what;
                    break;
                case DiffType.Change:
                    result += "Changed " + what;

                    string from = From.ToString();
                    string to = To.ToString();

                    string grouped = "from " + from + " to " + to;
                    if (grouped.Length < 18)
                        result += " " + grouped;
                    else
                        result += ' ' + NL +
                            "\tfrom: " + from + NL +
                            "\t  to: " + to + NL;

                    break;
                case DiffType.Remove:
                    result += "Removed " + what;
                    break;
                case DiffType.Rename:
                    string renameTo = '"' + To.ToString() + '"';
                    string renameTarget = '"' + Target.Name + '"';
                    
                    result += "Renamed " + Field + NL  + renameTarget + " to " + renameTo;
                    break;
                case DiffType.Merge:
                    result += "Merged";

                    if (From.Count > 1)
                    {
                        string prefix = "\t• ";
                        result += ':' + NL;

                        string listed = From.ListElements(NL, prefix);
                        string into = prefix + what;

                        result += listed + NL
                               + "  Into: " + NL
                               + prefix + what + NL;
                    }
                    else
                    {
                        string merged = From.ToString();
                        result += ' ' + merged + NL
                               + "\tinto: " + what + NL;
                    }

                    break;
            }

            if (children.Count > 0)
            {
                children.Sort();

                foreach (Diff child in children)
                {
                    result += NL;
                    result += child.WriteDiffTxt(detailed);
                }

                result += NL;
            }

            return result;
        }

        public override string ToString()
        {
            return WriteDiffTxt(Detailed);
        }

        public void WriteDiffHtml(ReflectionDumper buffer)
        {
            string diffType = Program.GetEnumName(Type);

            if (Type == DiffType.Add)
                diffType += "e";

            diffType += "d";

            if (HasParent)
                diffType += " child";

            buffer.OpenClassTag(diffType, stack, "div");
            buffer.NextLine();

            if (Type == DiffType.Change)
            {
                // Check if we should keep this on one line, based on the text version.
                string textSignature = WriteDiffTxt();
                bool multiline = textSignature.Contains(NL);

                // Write what we changed.
                buffer.WriteElement("WhatChanged", Field, stack + 1);

                // Write what was changed.
                Target.WriteHtml(buffer, stack + 1, false);

                // Changed From, Changed To.
                From.WriteHtml(buffer, multiline);
                To.WriteHtml(buffer, multiline);
            }
            else if (Type == DiffType.Rename)
            {
                // Write what we're renaming.
                buffer.OpenClassTag(Field, stack + 1);
                buffer.WriteElement("String", Target.Name, stack + 2);
                buffer.CloseClassTag(stack + 1);
                
                // Write its new name.
                To.WriteHtml(buffer);
            }
            else if (Type == DiffType.Merge)
            {
                if (From.Count > 1)
                {
                    // Write the elements that are being merged.
                    From.WriteHtml(buffer, false, 0, new Descriptor.HtmlConfig()
                    {
                        TagType = "li",
                        NumTabs = stack + 2,
                    });
                    
                    // Write what they merged into.
                    buffer.OpenClassTag("MergeListInto", stack + 1);
                    buffer.NextLine();

                    To.WriteHtml(buffer, false, 1, new Descriptor.HtmlConfig()
                    {
                        TagType = "li",
                        NumTabs = stack + 3,
                    });

                    buffer.CloseClassTag(stack + 1);
                }
                else
                {
                    // Write what we're merging.
                    Target.WriteHtml(buffer, stack + 1);

                    // Write what it merged into.
                    To.WriteHtml(buffer, true);
                }
            }
            else
            {
                string descType = Target.GetDescriptorType();
                bool detailed = (Type == DiffType.Add);

                if (Field != descType)
                {
                    if (Context != null && Context is Tags)
                    {
                        Tags tags = Context as Tags;
                        string tagClass = "TagChange";

                        if (tags.Count == 1)
                            tagClass += " singular";

                        if (Type == DiffType.Add)
                            tagClass += " to";
                        else
                            tagClass += " from";

                        buffer.OpenClassTag(tagClass, stack + 1);
                        buffer.NextLine();

                        tags.WriteHtml(buffer, stack + 2);
                        buffer.CloseClassTag(stack + 1);

                        detailed = false;
                    }
                    else
                    {
                        buffer.WriteElement("Field", Field, stack + 1);
                    }
                }

                buffer.OpenClassTag("Target", stack + 1);
                buffer.NextLine();

                Target.WriteHtml(buffer, stack + 2, detailed);
                buffer.CloseClassTag(stack + 1);
            }

            if (children.Count > 0)
            {
                children.Sort();
                children.ForEach(child => child.WriteDiffHtml(buffer));
            }

            buffer.CloseClassTag(stack, "div");
        }

        public int CompareTo(object obj)
        {
            if (obj.GetType() != GetType())
                throw new NotImplementedException("Diff can only be compared with another Diff");

            Diff diff = obj as Diff;

            // Try sorting by the type of diff.
            int sortByType = Type - diff.Type;
            if (sortByType != 0)
                return sortByType;

            // Try sorting by the field priority.
            var typePriority = ReflectionDatabase.TypePriority;
            int sortByField = 0;
            
            if (typePriority.Contains(Field) && typePriority.Contains(diff.Field))
                sortByField = typePriority.IndexOf(Field) - typePriority.IndexOf(diff.Field);
            else
                sortByField = Field.CompareTo(diff.Field);

            if (Type == DiffType.Change && Field == diff.Field)
            {
                string myType = Target.GetDescriptorType();
                string diffType = diff.Target.GetDescriptorType();

                int myPriority = typePriority.IndexOf(myType);
                int diffPriority = typePriority.IndexOf(diffType);

                if (myPriority >= 0)
                    myType = myPriority.ToString();

                if (diffPriority >= 0)
                    diffType = diffPriority.ToString();

                sortByField = myType.CompareTo(diffType);
            }
            
            if (sortByField != 0)
                return sortByField;

            // Try sorting by the targets.
            int sortByTarget = Target.CompareTo(diff.Target);

            if (sortByTarget != 0)
                return sortByTarget;

            // These are identical?
            return 0;
        }
    }
}
