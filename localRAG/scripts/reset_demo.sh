#!/bin/bash
echo "Cleaning up demo data..."

# Remove generated tags
if [ -f "tags.json" ]; then
    rm tags.json
    echo "Removed tags.json"
fi

# Remove vector DB and file storage
if [ -d "tmp-data" ]; then
    rm -rf tmp-data
    echo "Removed tmp-data/"
fi

# Remove traces
if [ -d "trace" ]; then
    rm -rf trace
    echo "Removed trace/"
fi

# Remove debug output if it exists
if [ -f "debug_output.txt" ]; then
    rm debug_output.txt
    echo "Removed debug_output.txt"
fi

echo "Cleanup complete. Ready for fresh start."
