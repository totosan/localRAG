use("localRAG");
db.getCollection("_ix__kernel_memory_single_index")
  .aggregate([
    {
      $project: {
        _id: 1,
        Index: 1,
        Payloads: {
          $filter: {
            input: "$Payloads",
            as: "payload",
            cond: {
              $eq: ["$$payload.Key", "file"],
            },
          },
        },
        Tags: {
          $filter: {
            input: "$Tags",
            as: "tag",
            cond: {
              $not: [{ $regexMatch: { input: "$$tag.Key", regex: /^__/ } }],
            },
          },
        },
      },
    },
    {
      $project: {
        _id: 0,
        Index: 1,
        file: { $arrayElemAt: ["$Payloads.Value", 0] },
        Tags: {
          $map: {
            input: "$Tags",
            as: "tag",
            in: {
              $arrayToObject: {
                $map: {
                  input: "$$tag.Values",
                  as: "value",
                  in: { k: "$$tag.Key", v: "$$value" },
                },
              },
            },
          },
        },
      },
    },
    {
      $group: {
        _id: "$file",
        Index: { $first: "$Index" },
        file: { $first: "$file" },
        Tags: { $first: "$Tags" },
      },
    },
    {
      $project: {
        _id: 0,
        Index: 1,
        file: 1,
        Tags: 1,
      },
    },
  ])
  .pretty();
