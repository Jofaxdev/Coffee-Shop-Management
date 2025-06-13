const fs = require("fs");
const path = require("path");

const ckeditorSource = path.join(
    __dirname,
    "node_modules",
    "@binay7587/ckeditor5-full-free/build/ckeditor.js"
);

const outputDir = path.join(__dirname, "dist");
const outputFile = path.join(outputDir, "ckeditor.min.js");

if (!fs.existsSync(outputDir)) {
    fs.mkdirSync(outputDir);
}

fs.copyFileSync(ckeditorSource, outputFile);

console.log("CKEditor has been built and copied to the dist folder.");
