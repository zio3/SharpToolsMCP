BEGIN {
    methods["GetMembers"] = 0
    methods["ViewDefinition"] = 0
    methods["FindReferences"] = 0
    methods["ListImplementations"] = 0
    methods["AnalyzeComplexity"] = 0
    methods["ManageUsings"] = 0
    methods["ManageAttributes"] = 0
    methods["SearchDefinitions"] = 0
}

/^\[McpServerTool.*Name = ToolHelpers\.SharpToolPrefix \+ nameof\(([^)]+)\)/ {
    match($0, /nameof\(([^)]+)\)/, arr)
    method = arr[1]
    if (method in methods) {
        methods[method] = NR - 1
    }
}

/^    }$/ {
    for (m in methods) {
        if (methods[m] > 0 && NR > methods[m]) {
            print m ": " methods[m] "-" NR
            methods[m] = 0
            break
        }
    }
}

/^}$/ {
    for (m in methods) {
        if (methods[m] > 0) {
            print m ": " methods[m] "-" NR
            methods[m] = 0
        }
    }
}
