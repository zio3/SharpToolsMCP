#\!/bin/bash

# Find method ranges by looking at decorators and next methods
echo "=== AnalysisTools.cs ==="
echo "GetMembers: Lines 155-349 (next method ViewDefinition starts at 350)"
echo "ViewDefinition: Lines 350-416 (next method ListImplementations starts at 417)"  
echo "ListImplementations: Lines 417-691 (next method FindReferences starts at 692)"
echo "FindReferences: Lines 692-1098 (next method SearchDefinitions starts at 1099)"
echo "SearchDefinitions: Lines 1099-1501 (already confirmed)"
echo "ManageUsings: Lines 1502-1616 (next method ManageAttributes starts at 1617)"
echo "ManageAttributes: Lines 1617-1720 (next method AnalyzeComplexity starts at 1721)"
echo "AnalyzeComplexity: Lines 1721-1814 (need to verify end)"
