<?php
/*
    POST release.php
    params: plugin_name, zip_file, manifest_file
*/
    $plugin_name = $_POST['plugin_name'];
    $zip_file = $_FILES['zip_file'];
    $manifest_file = $_FILES['manifest_file'];

    $phpFileUploadErrors = array(
        0 => 'There is no error, the file uploaded with success',
        1 => 'The uploaded file exceeds the upload_max_filesize directive in php.ini',
        2 => 'The uploaded file exceeds the MAX_FILE_SIZE directive that was specified in the HTML form',
        3 => 'The uploaded file was only partially uploaded',
        4 => 'No file was uploaded',
        6 => 'Missing a temporary folder',
        7 => 'Failed to write file to disk.',
        8 => 'A PHP extension stopped the file upload.',
    );

    if (!$plugin_name || !$zip_file || !$manifest_file) {
        http_response_code(400);
        die('Missing parameters');
    }

    if (!is_uploaded_file($zip_file['tmp_name']) || !is_uploaded_file($zip_file['tmp_name'])) {
        http_response_code(400);
        die('Wrong files');
    }

    if ($manifest_file['error'] !== 0) {
        http_response_code(400);
        die('Upload error: ' . $phpFileUploadErrors[$manifest_file['error']] . ' for file: ' . $manifest_file['name']);
    }
    if ($zip_file['error'] !== 0) {
        http_response_code(400);
        die('Upload error: ' . $phpFileUploadErrors[$zip_file['error']] . ' for file: ' . $zip_file['name']);
    }

    if (!move_uploaded_file($zip_file['tmp_name'], __DIR__ . '/' . $plugin_name . '/' . $zip_file['name'])) {
        http_response_code(400);
        die('Error moving file ' . $zip_file['name']);
    }
    if (!move_uploaded_file($manifest_file['tmp_name'], __DIR__ . '/' . $manifest_file['name'])) {
        http_response_code(400);
        die('Error moving file ' . $manifest_file['name']);
    }
    
    http_response_code(200);