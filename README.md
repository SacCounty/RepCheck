# RepCheck

The RepCheck utility was created as an alternative to IBM's Content Consistency Checker. This tool is designed to have better performance and work with higher volumes than the vendor supplied tool.

The goal is to track content consistency over time with the ability to automate as a scheduled task. Data on consistency is kept in a database for reporting.

 
## IMPORTANT

Pull the repo from the "master" branch.

## RepCheck 1.0: Released on 18 September 2015

RepCheck 1.0 contains all necessary features to check consistency in object stores using file stores and fixed content devices. Thank you to our growing community for your suggestions and contributions.

This release includes the following key new features:

1. Check for missing files in file stores and fixed content devices.
2. Check for orphaned files in files stores and fixed content devices.
3. Log information to Log4Net appender.
4. Store results in a database for reporting.

## Installation and Quick Start
### Requirements
* Microsoft .Net Framework 4.5.1 (https://www.microsoft.com/en-us/download/details.aspx?id=40779).
* Microsoft SQL Server (Required only for storing data for reporting).

### Installation 
1. Copy contents of the install folder to the desired location:
2. [optional] to create a database to keep report data, run the script (SQL Scripts\CreateRepCheckDatabase.sql).

_**NOTES:**_ In order to be able to easily get information on documents missing files, it is recommended the database be created in the same instance as the P8 object store databases. This will allow for joining to DocVersion tables to get index information.

##### Usage
RepCheck.exe /s dbserver\instance /os objectstore [/fs filestore] /ddb docDB /rdb reportDB [/t maxthreads] [/y]
	/? for help.
	/fs is an optional parameter for object stores with more than one file store.
    /t is an optional parameter for specifying a max thread count. MaxThreads setting in the config file will be used if this switch is not specified.
    /y Indicates to automatically upload report information to server (for unattended operation).

#### Query for missing and orphaned files.

If you created the database and have uploaded results, a sample query (SQL Scripts\RepCheck.sql) has been provided. This query will display counts of missing and orphaned files for each object store / file store combination.


## Resources

1.	County of Sacramento: http://www.saccounty.net
2.  Department of Technology: http://www.technology.saccounty.net/Pages/default.aspx

## Support

If you have any questions, please contact Guy Sperry (sperrygu@saccounty.net) or Jerry Gray (grayje@saccounty.net).


## Trademarks

FileNet is a trademark of IBM 
